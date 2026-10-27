namespace DepthCharts.Domain;

public sealed class DomainValidationException(string message) : Exception(message);
