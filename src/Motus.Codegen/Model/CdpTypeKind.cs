namespace Motus.Codegen.Model;

/// <summary>
/// Discriminates the kind of a CDP type definition.
/// </summary>
internal enum CdpTypeKind
{
    Object,
    StringEnum,
    Alias,
    ArrayType
}
