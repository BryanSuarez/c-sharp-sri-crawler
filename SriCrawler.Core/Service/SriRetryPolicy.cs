namespace DescagaCompronanteSRI.Services;

public static class SriRetryPolicy
{
    public const int QueryAttempts = 4;
    public const int DownloadAttempts = 3;

    public static TimeSpan CaptchaBackoff(int attempt) => TimeSpan.FromSeconds(4 * attempt);

    public static TimeSpan QueryBackoff(int attempt) => TimeSpan.FromSeconds(2 * attempt);

    public static TimeSpan DownloadBackoff(int attempt) => TimeSpan.FromMilliseconds(1_500 * attempt);
}
