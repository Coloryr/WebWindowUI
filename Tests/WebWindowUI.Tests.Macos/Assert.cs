namespace WebWindowUI.Tests.Macos;

/// <summary>
/// 轻量断言（本工程是独立可执行程序，不走 dotnet test，不引 xunit）。
/// 断言失败抛异常 → runner 捕获计 FAIL。
/// </summary>
internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "断言失败");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"断言失败：期望 {expected}，实际 {actual}");
    }
}
