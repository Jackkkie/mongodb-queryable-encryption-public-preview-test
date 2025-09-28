document.addEventListener('DOMContentLoaded', function() {
    // Show overlay initially and check status
    const overlay = document.getElementById('loading-overlay');
    if (overlay) {
        overlay.style.display = 'flex';
    }
    checkInitializationStatus();

    // Force YYYY-MM-DD format for date inputs
    const dateInputs = document.querySelectorAll('input[type="date"]');
    dateInputs.forEach(input => {
        input.addEventListener('focus', function(e) {
            e.target.type = 'text';
            e.target.placeholder = 'YYYY-MM-DD';
        });

        input.addEventListener('blur', function(e) {
            e.target.type = 'date';
            // Validate YYYY-MM-DD format
            const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
            if (e.target.value && !dateRegex.test(e.target.value)) {
                alert('Please use YYYY-MM-DD format');
                e.target.focus();
            }
        });

        input.addEventListener('input', function(e) {
            if (e.target.type === 'text') {
                // Auto-format as user types
                let value = e.target.value.replace(/\D/g, '');
                if (value.length >= 4) {
                    value = value.substring(0, 4) + '-' + value.substring(4);
                }
                if (value.length >= 7) {
                    value = value.substring(0, 7) + '-' + value.substring(7, 9);
                }
                e.target.value = value;
            }
        });
    });

    updatePatientCount();

    document.getElementById('addPatientForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        await addPatient();
    });

    document.getElementById('searchForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        await searchPatients();
    });

    document.getElementById('confirmGenerateBtn').addEventListener('click', async () => {
        await generatePatients();
    });

    document.getElementById('deleteAllBtn').addEventListener('click', async () => {
        if (confirm('Are you sure you want to delete all patients?')) {
            await deleteAllPatients();
        }
    });

    document.getElementById('clearSearchBtn').addEventListener('click', () => {
        document.getElementById('searchForm').reset();
        document.getElementById('resultsTableBody').innerHTML = '';
        document.getElementById('searchResultsSection').classList.add('d-none');
    });

    const searchFieldConfigs = {
        'searchZipCode': { minLength: 5, type: 'exact', autoSearch: true, debounce: 1000 },
        'searchYearOfBirth': { minLength: 4, type: 'range', autoSearch: true, debounce: 1000 },
        'searchFirstName': { minLength: 3, type: 'prefix', autoSearch: true, debounce: 1000 },
        'searchLastName': { minLength: 3, type: 'prefix', autoSearch: true, debounce: 1000 },
        'searchNationalId': { minLength: 6, type: 'prefix', autoSearch: true, debounce: 1000 },
        'searchPhoneNumber': { minLength: 4, type: 'suffix', autoSearch: true, debounce: 1000 },
        'searchNotes': { minLength: 3, type: 'substring', autoSearch: true, debounce: 1000 }
    };

    // Enable auto-search for configured fields with performance optimizations
    Object.keys(searchFieldConfigs).forEach(fieldId => {
        const config = searchFieldConfigs[fieldId];
        const element = document.getElementById(fieldId);

        if (element && config.autoSearch) {
            element.addEventListener('input', debounce(async (e) => {
                const value = e.target.value.trim();

                // Only search if we meet the minimum length requirement
                if (value.length >= config.minLength) {
                    await searchPatients();
                }
                // Also search when field is cleared (to refresh results)
                else if (value.length === 0) {
                    await searchPatients();
                }
            }, config.debounce));
        }
    });
});

function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

async function addPatient() {
    const patient = {
        firstName: document.getElementById('firstName').value,
        lastName: document.getElementById('lastName').value,
        dateOfBirth: document.getElementById('dob').value,
        zipCode: document.getElementById('zipCode').value,
        ssn: document.getElementById('nationalId').value,
        phoneNumber: document.getElementById('phoneNumber').value,
        notes: document.getElementById('notes').value
    };

    try {
        const response = await fetch('/api/patient/add', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(patient)
        });

        if (response.ok) {
            alert('Patient added successfully!');
            document.getElementById('addPatientForm').reset();
            updatePatientCount();
        } else {
            alert('Error adding patient');
        }
    } catch (error) {
        console.error('Error:', error);
        alert('Error adding patient');
    }
}

let currentExplainData = null;

async function searchPatients() {
    const searchRequest = {
        firstName: document.getElementById('searchFirstName').value,
        lastName: document.getElementById('searchLastName').value,
        yearOfBirth: document.getElementById('searchYearOfBirth').value || null,
        zipCode: document.getElementById('searchZipCode').value,
        nationalIdPrefix: document.getElementById('searchNationalId').value,
        phoneNumber: document.getElementById('searchPhoneNumber').value,
        notesKeyword: document.getElementById('searchNotes').value,
        includeExplain: true // Always get explain data
    };

    showLoadingSpinner(true);

    try {
        const response = await fetch('/api/patient/search', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(searchRequest)
        });

        if (response.ok) {
            const data = await response.json();

            const results = data.patients || data;
            displaySearchResults(results);

            if (data.explain) {
                currentExplainData = data.explain;
                document.getElementById('showExplainBtn').classList.remove('d-none');
                populateExplainModal(data.explain);
            } else {
                document.getElementById('showExplainBtn').classList.add('d-none');
            }
        } else {
            alert('Error searching patients');
        }
    } catch (error) {
        console.error('Error:', error);
        alert('Error searching patients');
    } finally {
        showLoadingSpinner(false);
    }
}

function displaySearchResults(results) {
    const tbody = document.getElementById('resultsTableBody');
    const noResults = document.getElementById('noResults');
    const searchResultsSection = document.getElementById('searchResultsSection');

    tbody.innerHTML = '';

    // Show the search results section when there are search attempts
    searchResultsSection.classList.remove('d-none');

    if (results.length === 0) {
        noResults.classList.remove('d-none');
    } else {
        noResults.classList.add('d-none');

        results.forEach(patient => {
            const row = tbody.insertRow();
            row.innerHTML = `
                <td>${patient.firstName}</td>
                <td>${patient.lastName}</td>
                <td>${new Date(patient.dateOfBirth).toISOString().split('T')[0]}</td>
                <td>${patient.zipCode}</td>
                <td>${patient.nationalId}</td>
                <td>${patient.phoneNumber}</td>
                <td>${patient.notes}</td>
            `;
        });
    }
}

let isGenerating = false;

async function generatePatients() {
    if (isGenerating) {
        return;
    }

    const countInput = document.getElementById('modalPatientCount');
    let count = parseInt(countInput.value);

    // Validate input
    if (isNaN(count) || count < 1000 || count > 5000000) {
        alert('Please enter a number between 1,000 and 5,000,000');
        countInput.focus();
        return;
    }

    // Round to nearest 1000
    count = Math.round(count / 1000) * 1000;
    countInput.value = count;

    if (!confirm(`This will generate ${count.toLocaleString()} patient records. Continue?`)) {
        return;
    }

    isGenerating = true;
    const btn = document.getElementById('confirmGenerateBtn');
    btn.disabled = true;
    btn.textContent = 'Generating...';

    // Close the modal
    const modal = bootstrap.Modal.getInstance(document.getElementById('generateModal'));
    if (modal) {
        modal.hide();
    }

    try {
        // Start the generation process
        const response = await fetch('/api/patient/generate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ count: count })
        });

        if (response.ok) {
            const data = await response.json();
            alert(`Successfully generated ${count.toLocaleString()} patients!\n\nDuration: ${data.duration || 'N/A'}\nRate: ${data.rate || 'N/A'}`);
            updatePatientCount();
        } else {
            alert('Error generating patients');
        }
    } catch (error) {
        console.error('Error:', error);
        alert('Error generating patients');
    } finally {
        isGenerating = false;
        btn.disabled = false;
        btn.textContent = 'Generate';
    }
}

async function deleteAllPatients() {
    try {
        const response = await fetch('/api/patient', {
            method: 'DELETE'
        });

        if (response.ok) {
            alert('All patients deleted successfully!');
            document.getElementById('resultsTableBody').innerHTML = '';
            updatePatientCount();
        } else {
            alert('Error deleting patients');
        }
    } catch (error) {
        console.error('Error:', error);
        alert('Error deleting patients');
    }
}

async function updatePatientCount() {
    try {
        const response = await fetch('/api/patient/count');
        if (response.ok) {
            const data = await response.json();
            document.getElementById('recordCount').textContent = data.count.toLocaleString();
        }
    } catch (error) {
        console.error('Error getting patient count:', error);
    }
}

function showLoadingSpinner(show) {
    const spinner = document.getElementById('loadingSpinner');
    if (show) {
        spinner.classList.remove('d-none');
    } else {
        spinner.classList.add('d-none');
    }
}

function populateExplainModal(explainData) {
    // Parse JSON string if needed
    if (typeof explainData === 'string') {
        try {
            explainData = JSON.parse(explainData);
        } catch (e) {
            console.error('Error parsing explain data:', e);
            explainData = {};
        }
    }

    // Update raw explain output - format it nicely
    const rawExplainEl = document.getElementById('modalRawExplain');
    if (rawExplainEl) {
        rawExplainEl.textContent = JSON.stringify(explainData, null, 2);
    }

    console.log('Explain modal populated with:', explainData);
}

async function checkInitializationStatus() {
    try {
        console.log('Checking MongoDB status...');
        const response = await fetch('/api/patient/status');

        if (response.ok) {
            const data = await response.json();
            console.log('Status response:', data);

            // Very explicit check for isInitialized
            if (data && data.isInitialized === true) {
                console.log('MongoDB is ready! Hiding overlay now...');
                hideLoadingOverlay();
                console.log('Status checking stopped');
                return; // Stop checking
            } else {
                console.log('MongoDB not ready yet, will check again in 2 seconds...');
                setTimeout(checkInitializationStatus, 2000);
            }
        } else {
            console.log(`Status request failed: ${response.status}, retrying in 2 seconds...`);
            setTimeout(checkInitializationStatus, 2000);
        }
    } catch (error) {
        console.error('Status check error:', error);
        setTimeout(checkInitializationStatus, 2000);
    }
}

function hideLoadingOverlay() {
    const overlay = document.getElementById('loading-overlay');
    if (overlay) {
        overlay.style.display = 'none';
        console.log('Loading overlay hidden');
    }
}

