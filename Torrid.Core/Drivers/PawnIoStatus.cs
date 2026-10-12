namespace Torrid.Core.Drivers;

public enum PawnIoState { NotInstalled, Outdated, Current, Unknown }

/// <summary>What is known about the PawnIO driver on this PC. <see cref="Version"/> is the installed version text, if any.</summary>
public sealed record PawnIoStatus(PawnIoState State, string? Version);
