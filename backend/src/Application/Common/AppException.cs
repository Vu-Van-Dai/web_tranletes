namespace ComicPlatform.Application.Common;

/// <summary>
/// Expected, user-facing failure. Mapped by the API to RFC 7807 ProblemDetails:
/// <c>status</c>, stable <c>code</c>, Vietnamese <c>detail</c> and optional field <c>errors</c>.
/// </summary>
public class AppException : Exception
{
    public AppException(int status, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Errors = errors;
    }

    public int Status { get; }
    public string Code { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}

public static class Errors
{
    public static AppException NotFound(string what = "Dữ liệu") =>
        new(404, "not_found", $"{what} không tồn tại hoặc đã bị xoá.");

    public static AppException Forbidden(string code = "forbidden", string message = "Bạn không có quyền thực hiện thao tác này.") =>
        new(403, code, message);

    public static AppException Unauthorized(string code = "unauthorized", string message = "Vui lòng đăng nhập để tiếp tục.") =>
        new(401, code, message);

    public static AppException Conflict(string code, string message) => new(409, code, message);

    public static AppException BadRequest(string code, string message) => new(400, code, message);

    public static AppException Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(400, "validation_failed", "Dữ liệu không hợp lệ, vui lòng kiểm tra lại.", errors);

    public static AppException Field(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    public static AppException PayloadTooLarge(string message) => new(413, "payload_too_large", message);

    public static AppException Unavailable(string code, string message) => new(503, code, message);

    public static AppException UpstreamFailed(string code, string message) => new(502, code, message);
}
