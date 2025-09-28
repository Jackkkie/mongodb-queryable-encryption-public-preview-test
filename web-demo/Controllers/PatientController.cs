using Microsoft.AspNetCore.Mvc;
using MongoQEDemo.Models;
using MongoQEDemo.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MongoQEDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PatientController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<PatientController> _logger;
        private static DateTime _generationStartTime;
        private static int _totalBatchesToProcess;
        private static int _completedBatches;
        private static readonly object _progressLock = new object();

        public PatientController(MongoDbService mongoDbService, ILogger<PatientController> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddPatient([FromBody] Patient patient)
        {
            try
            {
                if (patient == null)
                {
                    return BadRequest("Patient data is required");
                }

                var result = await _mongoDbService.InsertPatientAsync(patient);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding patient");
                return StatusCode(500, "An error occurred while adding the patient");
            }
        }

        [HttpPost("search")]
        public async Task<IActionResult> SearchPatients([FromBody] SearchRequest searchRequest)
        {
            try
            {
                DateTime? dobFrom = searchRequest.YearOfBirth.HasValue ? new DateTime(searchRequest.YearOfBirth.Value, 1, 1) : null;
                DateTime? dobTo = searchRequest.YearOfBirth.HasValue ? new DateTime(searchRequest.YearOfBirth.Value, 12, 31) : null;

                if (searchRequest.IncludeExplain)
                {
                    var (results, explainResult) = await _mongoDbService.SearchPatientsWithExplainAsync(
                        searchRequest.FirstName, searchRequest.LastName, dobFrom, dobTo,
                        searchRequest.ZipCode, searchRequest.NationalIdPrefix,
                        searchRequest.PhoneNumber, searchRequest.NotesKeyword);
                    return Ok(new { patients = results, explain = explainResult });
                }

                var searchResults = await _mongoDbService.SearchPatientsAsync(
                    searchRequest.FirstName, searchRequest.LastName, dobFrom, dobTo,
                    searchRequest.ZipCode, searchRequest.NationalIdPrefix,
                    searchRequest.PhoneNumber, searchRequest.NotesKeyword);
                return Ok(new { patients = searchResults });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching patients");
                return StatusCode(500, "An error occurred while searching patients");
            }
        }

        [HttpGet("count")]
        public async Task<IActionResult> GetPatientCount()
        {
            try
            {
                var count = await _mongoDbService.GetPatientCountAsync();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting patient count");
                return StatusCode(500, "An error occurred while getting the patient count");
            }
        }

        [HttpGet("status")]
        public IActionResult GetSystemStatus()
        {
            var status = new
            {
                isInitialized = MongoDbInitializationService.IsInitialized,
                status = MongoDbInitializationService.InitializationStatus,
                hasError = MongoDbInitializationService.InitializationError != null,
                error = MongoDbInitializationService.InitializationError?.Message,
                timestamp = DateTime.UtcNow
            };
            return Ok(status);
        }


        [HttpPost("generate")]
        public async Task<IActionResult> GeneratePatients([FromBody] GenerateRequest request)
        {
            try
            {
                if (request.Count < 1000 || request.Count > 5000000)
                {
                    return BadRequest(new { error = "Count must be between 1,000 and 5,000,000" });
                }

                request.Count = (request.Count / 500) * 500;

                var batchSize = 1000;
                var totalBatches = request.Count / batchSize;
                var remainingCount = request.Count % batchSize;
                var random = new Random();

                var maxParallelism = int.TryParse(Environment.GetEnvironmentVariable("MAX_BATCH_CONCURRENCY"), out var envConcurrency) ? envConcurrency : 32;

                _generationStartTime = DateTime.UtcNow;
                _totalBatchesToProcess = totalBatches;
                _completedBatches = 0;

                _logger.LogInformation($"Starting generation of {request.Count:N0} patients in {totalBatches:N0} batches of {batchSize:N0} with {maxParallelism} max parallelism at {_generationStartTime:yyyy-MM-dd HH:mm:ss} UTC");

                var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

                var batchTasks = new List<Task>();
                _logger.LogInformation($"Creating {totalBatches:N0} batch tasks for parallel execution...");

                for (int batch = 0; batch < totalBatches; batch++)
                {
                    var batchIndex = batch;
                    var task = ProcessBatchAsync(batchIndex, batchSize, random, semaphore, request.Count);
                    batchTasks.Add(task);

                    if ((batch + 1) % 100 == 0 || batch == totalBatches - 1)
                    {
                        _logger.LogInformation($"Created {batch + 1:N0}/{totalBatches:N0} batch tasks ({(double)(batch + 1)/totalBatches*100:F1}%)");
                    }
                }

                _logger.LogInformation($"All {totalBatches:N0} batch tasks created, starting parallel execution...");

                await Task.WhenAll(batchTasks);

                if (remainingCount > 0)
                {
                    await ProcessBatchAsync(totalBatches, remainingCount, random, semaphore, request.Count);
                }


                var totalElapsed = DateTime.UtcNow - _generationStartTime;
                var finalRate = request.Count / totalElapsed.TotalSeconds;
                _logger.LogInformation($"Successfully completed generation of {request.Count:N0} patients in {totalElapsed:hh\\:mm\\:ss} at rate {finalRate:F0} patients/sec");
                return Ok(new {
                    message = $"Successfully generated {request.Count:N0} patients",
                    duration = totalElapsed.ToString(@"hh\:mm\:ss"),
                    rate = $"{finalRate:F0} patients/sec"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating patients");
                return StatusCode(500, $"An error occurred while generating patients: {ex.Message}");
            }
        }

        private async Task HandleRetryWithBackoff(int batchIndex, int retry, int maxRetries, string errorType, string errorMessage, bool isServerSelectionTimeout = false, bool isNetworkError = false)
        {
            _logger.LogWarning($"[{errorType}] Error on batch {batchIndex}, retry {retry + 1}/{maxRetries}: {errorMessage}");

            var baseDelay = isServerSelectionTimeout || isNetworkError ? 5000 : 2000;
            var delay = Math.Pow(2, retry) * baseDelay;
            delay = Math.Min(delay, 60000);
            delay = Math.Min(delay, 60000);

            _logger.LogInformation($"Waiting {delay/1000:F1}s before retry {retry + 1} for batch {batchIndex} ({errorType})");
            await Task.Delay((int)delay);

            if (retry >= 1 && (isServerSelectionTimeout || isNetworkError))
            {
                _logger.LogWarning($"[CONNECTION_RESILIENCE] Applying connection resilience measures for batch {batchIndex} on retry {retry + 1}");

                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (isNetworkError && retry >= 2)
                {
                    var additionalDelay = 10000;
                    _logger.LogInformation($"[ATLAS_RECOVERY] Adding {additionalDelay/1000}s recovery delay for potential Atlas outage");
                    await Task.Delay(additionalDelay);
                }
            }

            if (retry >= 2)
            {
                _logger.LogWarning($"[RETRY_ESCALATION] Batch {batchIndex} on retry {retry + 1}/{maxRetries} - persistent {errorType}");
            }
        }

        private async Task ProcessBatchAsync(int batchIndex, int batchSize, Random random, SemaphoreSlim semaphore, int totalCount)
        {
            var batchStartTime = DateTime.UtcNow;
            _logger.LogInformation($"[BATCH_{batchIndex:D6}] Starting batch processing - Size: {batchSize}, Waiting for semaphore");

            await semaphore.WaitAsync();
            var semaphoreAcquiredTime = DateTime.UtcNow;
            var semaphoreWaitTime = semaphoreAcquiredTime - batchStartTime;

            _logger.LogInformation($"[BATCH_{batchIndex:D6}] Semaphore acquired after {semaphoreWaitTime.TotalMilliseconds:F0}ms, generating patients");

            try
            {
                var generationStartTime = DateTime.UtcNow;
                var patients = PatientGenerationService.GeneratePatientBatch(batchSize, new Random(random.Next() + batchIndex * 1000));
                var generationTime = DateTime.UtcNow - generationStartTime;

                _logger.LogInformation($"[BATCH_{batchIndex:D6}] Generated {patients.Count} patients in {generationTime.TotalMilliseconds:F0}ms, starting database insert");

                const int maxRetries = 5;
                bool batchSucceeded = false;
                var insertStartTime = DateTime.UtcNow;

                for (int retry = 0; retry < maxRetries; retry++)
                {
                    var attemptStartTime = DateTime.UtcNow;
                    _logger.LogInformation($"[BATCH_{batchIndex:D6}] Database insert attempt {retry + 1}/{maxRetries}");

                    try
                    {
                        await _mongoDbService.InsertManyPatientsAsync(patients);
                        var insertTime = DateTime.UtcNow - attemptStartTime;
                        var totalBatchTime = DateTime.UtcNow - batchStartTime;

                        _logger.LogInformation($"[BATCH_{batchIndex:D6}] Successfully inserted {patients.Count} patients - Insert: {insertTime.TotalMilliseconds:F0}ms, Total: {totalBatchTime.TotalMilliseconds:F0}ms");
                        batchSucceeded = true;
                        break;
                    }
                    catch (TimeoutException tex) when (retry < maxRetries - 1)
                    {
                        var attemptTime = DateTime.UtcNow - attemptStartTime;
                        var errorType = tex.Message.Contains("selecting a server") ? "CONNECTION_TIMEOUT" : "OPERATION_TIMEOUT";
                        _logger.LogWarning($"[BATCH_{batchIndex:D6}] {errorType} on attempt {retry + 1} after {attemptTime.TotalMilliseconds:F0}ms");
                        await HandleRetryWithBackoff(batchIndex, retry, maxRetries, errorType, tex.Message, isServerSelectionTimeout: errorType == "CONNECTION_TIMEOUT");
                    }
                    catch (Exception ex) when (retry < maxRetries - 1 && PatientGenerationService.IsRetriableError(ex))
                    {
                        var attemptTime = DateTime.UtcNow - attemptStartTime;
                        var errorType = PatientGenerationService.CategorizeError(ex);
                        _logger.LogWarning($"[BATCH_{batchIndex:D6}] {errorType} on attempt {retry + 1} after {attemptTime.TotalMilliseconds:F0}ms: {ex.Message}");
                        await HandleRetryWithBackoff(batchIndex, retry, maxRetries, errorType, ex.Message, isNetworkError: PatientGenerationService.IsNetworkRelatedError(ex));
                    }
                    catch (Exception ex) when (retry == maxRetries - 1)
                    {
                        var attemptTime = DateTime.UtcNow - attemptStartTime;
                        var errorType = PatientGenerationService.CategorizeError(ex);
                        _logger.LogError($"[BATCH_{batchIndex:D6}] [{errorType}] Final attempt failed after {attemptTime.TotalMilliseconds:F0}ms: {ex.Message}");
                        throw;
                    }
                }

                if (!batchSucceeded)
                {
                    var totalBatchTime = DateTime.UtcNow - batchStartTime;
                    _logger.LogError($"[BATCH_{batchIndex:D6}] Failed all {maxRetries} retry attempts after {totalBatchTime.TotalSeconds:F1}s total");
                    throw new Exception($"Failed to insert batch {batchIndex} after {maxRetries} attempts");
                }

                int completed, total;
                var completionTime = DateTime.UtcNow;
                var totalBatchDuration = completionTime - batchStartTime;

                lock (_progressLock)
                {
                    completed = ++_completedBatches;
                    total = _totalBatchesToProcess;
                }

                _logger.LogInformation($"[BATCH_{batchIndex:D6}] Completed successfully - Total duration: {totalBatchDuration.TotalMilliseconds:F0}ms, Progress: {completed}/{total} ({(double)completed/total*100:F1}%)");

                var progressPercent = (int)((double)completed / total * 100);
                var logInterval = Math.Max(1, total / 20);

                if (completed % logInterval == 0 || completed == total)
                {
                    var elapsed = DateTime.UtcNow - _generationStartTime;
                    var rate = completed * batchSize / elapsed.TotalSeconds;
                    var eta = completed < total ? TimeSpan.FromSeconds((total - completed) * batchSize / rate) : TimeSpan.Zero;

                    _logger.LogInformation($"Progress: {completed:N0}/{total:N0} batches ({progressPercent}%) | {completed * batchSize:N0} patients | Rate: {rate:F0}/sec | ETA: {eta:hh\\:mm\\:ss} | Avg batch time: {elapsed.TotalMilliseconds/completed:F0}ms");
                }

            }
            finally
            {
                var releaseTime = DateTime.UtcNow;
                var totalTime = releaseTime - batchStartTime;
                _logger.LogInformation($"[BATCH_{batchIndex:D6}] Releasing semaphore after {totalTime.TotalMilliseconds:F0}ms total execution");
                semaphore.Release();
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAllPatients()
        {
            try
            {
                await _mongoDbService.DeleteAllPatientsAsync();
                return Ok(new { message = "All patients deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting all patients");
                return StatusCode(500, "An error occurred while deleting patients");
            }
        }

        [HttpGet("schema")]
        public async Task<IActionResult> GetCollectionSchema()
        {
            try
            {
                var schema = await _mongoDbService.GetCollectionSchemaAsync();
                return Ok(schema);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting collection schema");
                return StatusCode(500, "An error occurred while getting collection schema");
            }
        }
    }
}