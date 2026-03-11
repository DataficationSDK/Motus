namespace Motus.Abstractions;

/// <summary>
/// Represents a geographic location.
/// </summary>
/// <param name="Latitude">The latitude in decimal degrees.</param>
/// <param name="Longitude">The longitude in decimal degrees.</param>
/// <param name="Accuracy">The accuracy of the location in meters.</param>
public sealed record Geolocation(double Latitude, double Longitude, double? Accuracy = null);
