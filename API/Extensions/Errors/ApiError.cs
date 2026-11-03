namespace PlaymoxAPI.Extensions.Errors;

public class ApiError
{
    public int Status { get; set; }
    public string Error { get; set; }
    public string Message { get; set; }
    public string Path { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}