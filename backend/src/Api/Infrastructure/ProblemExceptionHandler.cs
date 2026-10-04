using System.Diagnostics;
using ComicPlatform.Application.Common;
using ComicPlatform.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicPlatform.Api.Infrastructure;

/// <summary>
/// Maps exceptions to RFC 7807 ProblemDetails with a stable <c>code</c> and Vietnamese <c>detail</c>.
/// Unexpected exceptions are logged with a trace id and never leak internals.
/// </summary>
public sealed class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to send.
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, code, detail, errors) = exception switch
        {
            AppException app => (app.Status, app.Code, app.Message, app.Errors),
            DomainException domain => (StatusCodes.Status409Conflict, domain.Code, domain.Message, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency_conflict",
                "Dữ liệu vừa được thay đổi bởi người khác. Vui lòng tải lại và thử lại.", null),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "already_exists", "Dữ liệu bị trùng với một bản ghi đã có.", null),
            BadHttpRequestException bad when bad.StatusCode == StatusCodes.Status413PayloadTooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "payload_too_large", "Tệp tải lên vượt quá dung lượng cho phép.", null),
            BadHttpRequestException bad => (bad.StatusCode, "bad_request", "Yêu cầu không hợp lệ.", (IReadOnlyDictionary<string, string[]>?)null),
            TimeoutException or OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "timeout",
                "Yêu cầu xử lý quá lâu, vui lòng thử lại.", null),
            _ => (StatusCodes.Status500InternalServerError, "internal_error",
                "Đã xảy ra lỗi không mong muốn. Vui lòng thử lại sau.", null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else if (status is 401 or 403)
        {
            logger.LogWarning("Request denied {Status} {Code} for {Method} {Path}", status, code, context.Request.Method, context.Request.Path);
        }

        await WriteAsync(context, status, code, detail, errors);
        return true;
    }

    public static async Task WriteAsync(HttpContext context, int status, string code, string detail, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(status),
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }

    private static string TitleFor(int status) => status switch
    {
        400 => "Yêu cầu không hợp lệ",
        401 => "Chưa đăng nhập",
        403 => "Không có quyền",
        404 => "Không tìm thấy",
        409 => "Xung đột dữ liệu",
        413 => "Dữ liệu quá lớn",
        429 => "Quá nhiều yêu cầu",
        502 => "Lỗi dịch vụ lưu trữ",
        503 => "Dịch vụ tạm thời không khả dụng",
        504 => "Hết thời gian xử lý",
        _ => "Lỗi hệ thống",
    };
}
