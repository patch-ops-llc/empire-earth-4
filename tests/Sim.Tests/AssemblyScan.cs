using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Sim.Tests;

/// <summary>
/// Reads a compiled assembly's metadata and instructions and reports every use of a set of types.
/// It looks at each field, property, method signature, local variable and instruction, at every
/// type and member an instruction refers to, and then at everything the assembly refers to at all.
/// </summary>
internal sealed class AssemblyScan
{
    public static readonly ImmutableHashSet<string> FloatingPointTypes =
    [
        "System.Single",
        "System.Double",
        "System.Decimal",
        "System.Half",
        "System.MathF",
        "System.Runtime.InteropServices.NFloat",
        "System.Numerics.Complex",
        "System.Numerics.Matrix3x2",
        "System.Numerics.Matrix4x4",
        "System.Numerics.Plane",
        "System.Numerics.Quaternion",
        "System.Numerics.Vector2",
        "System.Numerics.Vector3",
        "System.Numerics.Vector4",
    ];

    public static readonly ImmutableHashSet<string> RandomTypes = ["System.Random"];

    private static readonly ImmutableHashSet<string> FloatingPointInstructions =
    [
        "ldc.r4", "ldc.r8", "conv.r4", "conv.r8", "conv.r.un",
        "ldelem.r4", "ldelem.r8", "stelem.r4", "stelem.r8",
        "ldind.r4", "ldind.r8", "stind.r4", "stind.r8",
    ];

    private static readonly Dictionary<int, OpCode> Instructions = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(instruction => (int)unchecked((ushort)instruction.Value));

    private readonly MetadataReader _reader;
    private readonly TypeNames _names;
    private readonly ImmutableHashSet<string> _forbidden;
    private readonly bool _reportFloatingPointInstructions;
    private readonly List<string> _findings = [];
    private int _methodBodies;

    private AssemblyScan(MetadataReader reader, ImmutableHashSet<string> forbidden, bool reportFloatingPointInstructions)
    {
        _reader = reader;
        _names = new TypeNames();
        _forbidden = forbidden;
        _reportFloatingPointInstructions = reportFloatingPointInstructions;
    }

    public static (IReadOnlyList<string> Findings, int MethodBodies) Run(
        Assembly assembly, ImmutableHashSet<string> forbidden, bool reportFloatingPointInstructions)
    {
        using var file = File.OpenRead(assembly.Location);
        using var image = new PEReader(file);
        var scan = new AssemblyScan(image.GetMetadataReader(), forbidden, reportFloatingPointInstructions);
        scan.ScanDefinitions(image);
        scan.ScanReferences();
        return (scan._findings, scan._methodBodies);
    }

    private void ScanDefinitions(PEReader image)
    {
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(typeHandle);
            var typeName = _names.GetTypeFromDefinition(_reader, typeHandle, 0);

            Report(typeName, "base type", () => Name(type.BaseType));
            foreach (var implementation in type.GetInterfaceImplementations())
            {
                Report(typeName, "interface", () => Name(_reader.GetInterfaceImplementation(implementation).Interface));
            }

            foreach (var fieldHandle in type.GetFields())
            {
                var field = _reader.GetFieldDefinition(fieldHandle);
                Report($"{typeName}.{_reader.GetString(field.Name)}", "field of", () => field.DecodeSignature(_names, null));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = _reader.GetPropertyDefinition(propertyHandle);
                Report($"{typeName}.{_reader.GetString(property.Name)}", "property of", () => Describe(property.DecodeSignature(_names, null)));
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = _reader.GetMethodDefinition(methodHandle);
                var location = $"{typeName}.{_reader.GetString(method.Name)}";
                Report(location, "signature", () => Describe(method.DecodeSignature(_names, null)));

                if (method.RelativeVirtualAddress != 0)
                {
                    ScanBody(location, image.GetMethodBody(method.RelativeVirtualAddress));
                }
            }
        }
    }

    private void ScanBody(string location, MethodBodyBlock body)
    {
        _methodBodies++;
        if (!body.LocalSignature.IsNil)
        {
            Report(location, "local variable of", () => string.Join(", ", _reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(_names, null)));
        }

        var code = body.GetILReader();
        while (code.RemainingBytes > 0)
        {
            int value = code.ReadByte();
            if (value == 0xFE)
            {
                value = 0xFE00 | code.ReadByte();
            }

            var instruction = Instructions[value];
            if (_reportFloatingPointInstructions && FloatingPointInstructions.Contains(instruction.Name!))
            {
                _findings.Add($"{location}: instruction {instruction.Name}");
            }

            switch (instruction.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    code.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    code.Offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    code.Offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    var targets = code.ReadInt32();
                    code.Offset += 4 * targets;
                    break;
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineTok or OperandType.InlineType:
                    var operand = MetadataTokens.EntityHandle(code.ReadInt32());
                    Report(location, "use of", () => Name(operand));
                    break;
                default:
                    code.Offset += 4;
                    break;
            }
        }
    }

    /// <summary>Everything the assembly refers to, whether or not an instruction was seen using it.</summary>
    private void ScanReferences()
    {
        foreach (var handle in _reader.TypeReferences)
        {
            Report("Assembly", "reference to type", () => Name(handle));
        }

        foreach (var handle in _reader.MemberReferences)
        {
            Report("Assembly", "reference to member", () => Name(handle));
        }

        for (var row = 1; row <= _reader.GetTableRowCount(TableIndex.TypeSpec); row++)
        {
            Report("Assembly", "reference to type", () => Name(MetadataTokens.TypeSpecificationHandle(row)));
        }

        for (var row = 1; row <= _reader.GetTableRowCount(TableIndex.MethodSpec); row++)
        {
            Report("Assembly", "reference to member", () => Name(MetadataTokens.MethodSpecificationHandle(row)));
        }

        for (var row = 1; row <= _reader.GetTableRowCount(TableIndex.StandAloneSig); row++)
        {
            var signature = _reader.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(row));
            Report("Assembly", "signature", () => signature.GetKind() == StandaloneSignatureKind.LocalVariables
                ? string.Join(", ", signature.DecodeLocalSignature(_names, null))
                : Describe(signature.DecodeMethodSignature(_names, null)));
        }
    }

    /// <summary>
    /// Runs something that names types, and adds a finding for each forbidden type it named.
    /// </summary>
    private void Report(string location, string what, Func<string?> describe)
    {
        _names.Seen.Clear();
        var description = describe();
        foreach (var name in _names.Seen.Distinct().Where(_forbidden.Contains).ToArray())
        {
            _findings.Add($"{location}: {what} {description} (forbidden: {name})");
        }
    }

    /// <summary>The name of whatever a metadata token points at, visiting every type in its signature.</summary>
    private string? Name(EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return null;
        }

        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                return _names.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0);
            case HandleKind.TypeSpecification:
                return _names.GetTypeFromSpecification(_reader, null, (TypeSpecificationHandle)handle, 0);
            case HandleKind.MemberReference:
                var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
                var signature = member.GetKind() == MemberReferenceKind.Field
                    ? member.DecodeFieldSignature(_names, null)
                    : Describe(member.DecodeMethodSignature(_names, null));
                return $"{Name(member.Parent)}.{_reader.GetString(member.Name)} [{signature}]";
            case HandleKind.MethodSpecification:
                var specification = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var arguments = specification.DecodeSignature(_names, null);
                return $"{Name(specification.Method)}<{string.Join(", ", arguments)}>";
            case HandleKind.StandaloneSignature:
                return Describe(_reader.GetStandaloneSignature((StandaloneSignatureHandle)handle).DecodeMethodSignature(_names, null));
            default:
                // A type, method or field defined in the scanned assembly itself: scanned where it is defined.
                return null;
        }
    }

    private static string Describe(MethodSignature<string> signature) =>
        $"{signature.ReturnType} ({string.Join(", ", signature.ParameterTypes)})";

    /// <summary>Turns type signatures into names, and remembers every plain type it comes across.</summary>
    private sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        public List<string> Seen { get; } = [];

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => See("System." + typeCode);

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            return See(declaring.IsNil
                ? Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name))
                : GetTypeFromDefinition(reader, declaring, 0) + "+" + reader.GetString(type.Name));
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return See(type.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0) + "+" + reader.GetString(type.Name)
                : Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name)));
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString(CultureInfo.InvariantCulture);

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString(CultureInfo.InvariantCulture);

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "function " + Describe(signature);

        private static string Qualify(string @namespace, string name) => @namespace.Length == 0 ? name : @namespace + "." + name;

        private string See(string name)
        {
            Seen.Add(name);
            return name;
        }
    }
}
