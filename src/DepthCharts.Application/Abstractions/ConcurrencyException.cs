namespace DepthCharts.Application.Abstractions;

public sealed class ConcurrencyException(string message) : Exception(message);
