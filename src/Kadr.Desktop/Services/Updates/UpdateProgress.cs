namespace Kadr.Desktop.Services.Updates;

public sealed record UpdateProgress(double Percentage, bool IsVerifying = false);
