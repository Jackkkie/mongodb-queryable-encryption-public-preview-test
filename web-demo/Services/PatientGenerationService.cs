using MongoQEDemo.Models;

namespace MongoQEDemo.Services
{
    public class PatientGenerationService
    {
        public static List<Patient> GeneratePatientBatch(int count, Random random)
        {
            var firstNames = new[] {
                "John", "Jane", "Michael", "Sarah", "David", "Emily", "James", "Lisa", "Robert", "Mary",
                "William", "Patricia", "Richard", "Jennifer", "Thomas", "Elizabeth", "Charles", "Linda", "Daniel", "Barbara",
                "Christopher", "Susan", "Matthew", "Jessica", "Anthony", "Karen", "Mark", "Nancy", "Donald", "Betty",
                "Steven", "Dorothy", "Paul", "Helen", "Andrew", "Sandra", "Joshua", "Donna", "Kenneth", "Carol",
                "Kevin", "Ruth", "Brian", "Sharon", "George", "Michelle", "Timothy", "Laura", "Ronald", "Amy",
                "Jason", "Kimberly", "Edward", "Deborah", "Jeffrey", "Angela", "Ryan", "Brenda", "Jacob", "Emma",
                "Gary", "Olivia", "Nicholas", "Cynthia", "Eric", "Marie", "Jonathan", "Janet", "Stephen", "Frances",
                "Larry", "Catherine", "Justin", "Samantha", "Scott", "Debra", "Brandon", "Rachel", "Benjamin", "Carolyn",
                "Samuel", "Virginia", "Gregory", "Maria", "Alexander", "Heather", "Patrick", "Diane", "Frank", "Julie",
                "Raymond", "Joyce", "Jack", "Victoria", "Dennis", "Kelly", "Jerry", "Christina", "Tyler", "Joan",
                "Aaron", "Evelyn", "Jose", "Judith", "Henry", "Megan", "Adam", "Cheryl", "Douglas", "Andrea",
                "Nathan", "Hannah", "Peter", "Jacqueline", "Zachary", "Martha", "Kyle", "Gloria", "Noah", "Teresa",
                "Alan", "Sara", "Carl", "Janice", "Jordan", "Rose", "Wayne", "Julia", "Ralph", "Alice",
                "Roy", "Jean", "Eugene", "Lois", "Louis", "Louise", "Philip", "Isabella", "Bobby", "Sophia",
                "Johnny", "Amelia", "Mason", "Harper", "Logan", "Evelyn", "Elijah", "Abigail", "Oliver", "Ella",
                "Lucas", "Avery", "Ethan", "Sofia", "Sebastian", "Camila", "Owen", "Aria", "Liam", "Scarlett",
                "Carter", "Victoria", "Wyatt", "Madison", "Luke", "Luna", "Grayson", "Grace", "Leo", "Chloe",
                "Lincoln", "Penelope", "Gabriel", "Layla", "Isaiah", "Riley", "Maverick", "Zoey", "Hunter", "Nora",
                "Elias", "Lily", "Aaron", "Eleanor", "Connor", "Lillian", "Hudson", "Addison", "Caleb", "Aubrey"
            };

            var lastNames = new[] {
                "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
                "Anderson", "Taylor", "Thomas", "Moore", "Jackson", "Martin", "Lee", "Thompson", "White", "Harris",
                "Clark", "Lewis", "Robinson", "Walker", "Perez", "Hall", "Young", "Allen", "Sanchez", "Wright",
                "King", "Scott", "Green", "Baker", "Adams", "Nelson", "Hill", "Ramirez", "Campbell", "Mitchell",
                "Roberts", "Carter", "Phillips", "Evans", "Turner", "Torres", "Parker", "Collins", "Edwards", "Stewart",
                "Morris", "Rogers", "Reed", "Cook", "Morgan", "Bell", "Murphy", "Bailey", "Rivera", "Cooper",
                "Richardson", "Cox", "Howard", "Ward", "Peterson", "Gray", "James", "Watson", "Brooks", "Kelly",
                "Sanders", "Price", "Bennett", "Wood", "Barnes", "Ross", "Henderson", "Coleman", "Jenkins", "Perry",
                "Powell", "Long", "Patterson", "Hughes", "Flores", "Washington", "Butler", "Simmons", "Foster",
                "Gonzales", "Bryant", "Alexander", "Russell", "Griffin", "Diaz", "Hayes", "Myers", "Ford", "Hamilton",
                "Graham", "Sullivan", "Wallace", "Woods", "Cole", "West", "Jordan", "Owens", "Reynolds", "Fisher",
                "Ellis", "Harrison", "Gibson", "McDonald", "Cruz", "Marshall", "Ortiz", "Gomez", "Murray", "Freeman",
                "Wells", "Webb", "Simpson", "Stevens", "Tucker", "Porter", "Hunter", "Hicks", "Crawford", "Henry",
                "Boyd", "Mason", "Morales", "Kennedy", "Warren", "Dixon", "Ramos", "Reyes", "Burns", "Gordon",
                "Shaw", "Holmes", "Rice", "Robertson", "Hunt", "Black", "Daniels", "Palmer", "Mills", "Nichols"
            };

            var conditions = new[] {
                "HTN", "DM2", "Asthma", "Arthritis", "CAD", "Migraine", "Allergies", "Back Pain", "Depression", "Anxiety",
                "Cholesterol", "Obesity", "Sleep Apnea", "GERD", "Osteoporosis", "Fibromyalgia", "CKD", "Thyroid", "COPD",
                "Psoriasis", "Bipolar", "Celiac", "Crohn's", "MS", "Epilepsy", "Lupus", "Parkinson's", "Dementia",
                "Stroke Hx", "Cancer Hx", "DM1", "Pneumonia", "Bronchitis", "Sinusitis", "Tinnitus", "Glaucoma",
                "Cataracts", "AMD", "Hearing Loss", "CTS", "Tendonitis", "Bursitis", "Sciatica", "Disc Herniation",
                "Scoliosis", "OA", "RA", "Gout", "Anemia", "Iron Def", "B12 Def", "Thyroid Nodules", "Gallstones",
                "Kidney Stones", "UTI Hx", "IBS", "Lactose Int", "Food Allergy", "Drug Allergy", "Skin Allergy",
                "Eczema", "Dermatitis", "Acne", "Rosacea", "Varicose", "DVT", "AFib", "Murmur", "Pacemaker",
                "Stent", "CABG", "Appendectomy", "Hernia", "Cholecystectomy", "Cataract Sx", "Knee Replace", "Hip Replace"
            };

            var zipRanges = new[] { 10000, 20000, 30000, 40000, 50000, 60000, 70000 };
            var zipMax = new[] { 19999, 29999, 39999, 49999, 59999, 69999, 99999 };

            var simpleTemplates = new[] {
                "Patient has {0} condition",
                "{0} currently being treated",
                "History of {0} documented",
                "Active {0} requiring monitoring",
                "Chronic {0} with stable symptoms",
                "Mild {0} with good prognosis",
                "Severe {0} needs close follow-up",
                "Recent diagnosis of {0}",
                "Family history of {0}",
                "Stable {0} on current treatment",
                "Ongoing {0} management plan",
                "{0} responding well to therapy"
            };

            var dualTemplates = new[] {
                "{0} and {1} co-occurring",
                "{0} with secondary {1}",
                "Primary {0}, also has {1}",
                "{0} plus {1} complications",
                "Managing {0} and {1}",
                "{0} stable, {1} improving",
                "Both {0} and {1} active",
                "{0} controlled, new {1}",
                "Chronic {0} with acute {1}",
                "{0} treated, monitoring {1}"
            };

            var patients = new List<Patient>(count);

            Span<int> firstNameIdx = stackalloc int[Math.Min(count, 1000)];
            Span<int> lastNameIdx = stackalloc int[Math.Min(count, 1000)];
            Span<int> condition1Idx = stackalloc int[Math.Min(count, 1000)];
            Span<int> condition2Idx = stackalloc int[Math.Min(count, 1000)];

            var batchSize = Math.Min(count, 1000);
            var remaining = count;

            while (remaining > 0)
            {
                var currentBatch = Math.Min(remaining, batchSize);

                for (int i = 0; i < currentBatch; i++)
                {
                    firstNameIdx[i] = random.Next(firstNames.Length);
                    lastNameIdx[i] = random.Next(lastNames.Length);
                    condition1Idx[i] = random.Next(conditions.Length);
                    condition2Idx[i] = random.Next(conditions.Length);
                }

                for (int i = 0; i < currentBatch; i++)
                {
                    var firstName = firstNames[firstNameIdx[i]];
                    var lastName = lastNames[lastNameIdx[i]];
                    var condition1 = conditions[condition1Idx[i]];
                    var condition2 = conditions[condition2Idx[i]];

                    var birthYear = random.Next(1925, 2025);
                    var birthMonth = random.Next(1, 13);
                    var birthDay = random.Next(1, 29);
                    var birthDate = new DateTime(birthYear, birthMonth, birthDay);

                    var zipRegion = random.Next(7);
                    var zipCode = random.Next(zipRanges[zipRegion], zipMax[zipRegion] + 1).ToString();

                    var shortYear = birthYear % 100;
                    var genderDigit = random.Next(1, 5);
                    var serialNumber = random.Next(1000000, 10000000);
                    var nationalId = $"{shortYear:D2}{birthMonth:D2}{birthDay:D2}-{genderDigit}{serialNumber:D6}";

                    var phoneNumber = $"010-{random.Next(1000, 10000):D4}-{random.Next(1000, 10000):D4}";

                    string notes;
                    if (condition1 == condition2 || random.Next(3) == 0)
                    {
                        var template = simpleTemplates[random.Next(simpleTemplates.Length)];
                        notes = string.Format(template, condition1);
                    }
                    else
                    {
                        var template = dualTemplates[random.Next(dualTemplates.Length)];
                        notes = string.Format(template, condition1, condition2);
                    }

                    if (notes.Length > 60)
                    {
                        notes = notes.Substring(0, 57) + "...";
                    }

                    if (notes.Length > 60)
                    {
                        notes = notes.Substring(0, 60);
                    }

                    patients.Add(new Patient
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        DateOfBirth = birthDate,
                        ZipCode = zipCode,
                        NationalId = nationalId,
                        PhoneNumber = phoneNumber,
                        Notes = notes
                    });
                }

                remaining -= currentBatch;
            }

            return patients;
        }

        public static string CategorizeError(Exception ex)
        {
            var message = ex.Message.ToLower();
            var exceptionType = ex.GetType().Name;

            if (message.Contains("timeout") || message.Contains("timed out"))
                return "TIMEOUT_ERROR";
            if (message.Contains("network") || message.Contains("connection"))
                return "NETWORK_ERROR";
            if (message.Contains("not primary") || message.Contains("primary"))
                return "REPLICA_SET_ERROR";
            if (message.Contains("cancelled") || message.Contains("canceled"))
                return "OPERATION_CANCELLED";
            if (message.Contains("pool") || message.Contains("connection pool"))
                return "CONNECTION_POOL_ERROR";
            if (message.Contains("authentication") || message.Contains("auth"))
                return "AUTH_ERROR";
            if (message.Contains("duplicate") || message.Contains("unique"))
                return "DATA_CONSTRAINT_ERROR";
            if (exceptionType == "MongoConnectionException")
                return "MONGO_CONNECTION_ERROR";
            if (exceptionType == "MongoServerException")
                return "MONGO_SERVER_ERROR";
            if (exceptionType == "MongoNetworkException")
                return "MONGO_NETWORK_ERROR";

            return "UNKNOWN_ERROR";
        }

        public static bool IsRetriableError(Exception ex)
        {
            var message = ex.Message.ToLower();
            var exceptionType = ex.GetType().Name;

            return message.Contains("timeout") ||
                   message.Contains("timed out") ||
                   message.Contains("network") ||
                   message.Contains("connection") ||
                   message.Contains("not primary") ||
                   message.Contains("primary") ||
                   message.Contains("pool") ||
                   message.Contains("server returned") ||
                   message.Contains("heartbeat") ||
                   exceptionType.Contains("MongoConnection") ||
                   exceptionType.Contains("MongoNetwork") ||
                   exceptionType == "TimeoutException";
        }

        public static bool IsNetworkRelatedError(Exception ex)
        {
            var message = ex.Message.ToLower();
            var exceptionType = ex.GetType().Name;

            return message.Contains("network") ||
                   message.Contains("connection") ||
                   message.Contains("heartbeat") ||
                   message.Contains("disconnected") ||
                   exceptionType.Contains("MongoConnection") ||
                   exceptionType.Contains("MongoNetwork");
        }
    }
}