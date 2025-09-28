namespace WpAiCli.Configuration;

public enum ExitCode
{
    Success = 0,
    InvalidArguments = 1,
    MissingConfiguration = 2,
    ApiError = 3,
    UnhandledError = 99
}
