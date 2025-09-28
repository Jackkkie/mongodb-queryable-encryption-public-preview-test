namespace MongoQEDemo.Models
{
    public class SearchRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? YearOfBirth { get; set; }
        public string? ZipCode { get; set; }
        public string? NationalIdPrefix { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NotesKeyword { get; set; }
        public bool IncludeExplain { get; set; } = false;
    }

    public class GenerateRequest
    {
        public int Count { get; set; } = 20000;
    }
}