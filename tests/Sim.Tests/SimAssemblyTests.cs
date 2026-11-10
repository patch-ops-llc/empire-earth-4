using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Sim.Tests;

public class SimAssemblyTests
{
    [Fact]
    public void SimReferencesOnlyTheDotNetBaseLibraries()
    {
        var references = typeof(SimInfo).Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(references);
        Assert.All(references, reference =>
            Assert.True(
                reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"Sim must not depend on '{reference.Name}'. It may only use the .NET base libraries, never Godot."));
    }

    /// <summary>
    /// Determinism rule 1. Floating-point results can differ between processors and operating
    /// systems, and one differing bit is a desync. The scan reads the compiled assembly, so it also
    /// sees what the compiler generated and what a method only uses in passing.
    /// </summary>
    [Fact]
    public void SimUsesNoFloatingPointTypes()
    {
        var scan = AssemblyScan.Run(typeof(SimInfo).Assembly, AssemblyScan.FloatingPointTypes, reportFloatingPointInstructions: true);

        Assert.True(scan.MethodBodies >= 50, $"The scan read only {scan.MethodBodies} method bodies of Sim, so it proves nothing.");
        Assert.True(
            scan.Findings.Count == 0,
            "Sim must not use float, double, decimal or any other floating-point type (docs/determinism.md, rule 1). "
            + "Use Sim.Numerics.Fixed or Sim.Numerics.Fraction.\n" + string.Join("\n", scan.Findings));
    }

    /// <summary>Determinism rule 3. The only random generator is the seeded one the world owns.</summary>
    [Fact]
    public void SimDoesNotUseSystemRandom()
    {
        var scan = AssemblyScan.Run(typeof(SimInfo).Assembly, AssemblyScan.RandomTypes, reportFloatingPointInstructions: false);

        Assert.True(
            scan.Findings.Count == 0,
            "Sim must not use System.Random (docs/determinism.md, rule 3). Use World.Random.\n" + string.Join("\n", scan.Findings));
    }

    /// <summary>
    /// A scan that finds nothing is only worth something if it would have found something. This
    /// runs it over samples in this test assembly, one for each way a floating-point type can
    /// get into compiled code.
    /// </summary>
    [Fact]
    public void TheScanFindsEveryWayAFloatingPointTypeCanBeUsed()
    {
        var scan = AssemblyScan.Run(typeof(SimAssemblyTests).Assembly, AssemblyScan.FloatingPointTypes, reportFloatingPointInstructions: true);
        var prefix = typeof(FloatingPointSamples).FullName + ".";

        bool Found(string member, params string[] parts) => scan.Findings.Any(finding =>
            finding.StartsWith(prefix + member + ":", StringComparison.Ordinal) && parts.All(finding.Contains));

        Assert.True(Found(nameof(FloatingPointSamples.Field), "field of", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.Property), "property of", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.Parameter), "signature", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.ReturnValue), "signature", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.Local), "local variable", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.ArithmeticOnly), "instruction conv.r8"));
        Assert.True(Found(nameof(FloatingPointSamples.ArithmeticOnly), "instruction ldc.r8"));
        Assert.True(Found(nameof(FloatingPointSamples.CallOnly), "use of", "System.Math.Sign", "System.Double"));
        Assert.True(Found(nameof(FloatingPointSamples.GenericType), "use of", "List`1<System.Single>"));
        Assert.True(Found(nameof(FloatingPointSamples.GenericMethod), "use of", "Empty", "<System.Double>"));
        Assert.True(Found(nameof(FloatingPointSamples.ArrayOfFloats), "use of", "System.Single"));
        Assert.True(Found(nameof(FloatingPointSamples.DecimalNumber), "use of", "System.Decimal"));
        Assert.True(Found(nameof(FloatingPointSamples.TypeOf), "use of", "System.Half"));

        // The compiler moves a lambda into a class of its own, nested in the one it was written in.
        Assert.Contains(scan.Findings, finding =>
            finding.StartsWith(typeof(FloatingPointSamples).FullName + "+", StringComparison.Ordinal)
            && finding.Contains(nameof(FloatingPointSamples.Lambda)) && finding.Contains("instruction conv.r8"));

        // The part of the scan that is not tied to a method: everything the assembly refers to.
        Assert.Contains(scan.Findings, finding => finding.StartsWith("Assembly: reference to type System.Decimal", StringComparison.Ordinal));
        Assert.Contains(scan.Findings, finding => finding.StartsWith("Assembly: reference to member System.Math.Sign", StringComparison.Ordinal));

        // And it does not cry wolf over whole-number code.
        Assert.DoesNotContain(scan.Findings, finding => finding.Contains(nameof(FloatingPointSamples.WholeNumbersOnly)));
    }

    [Fact]
    public void TheScanFindsSystemRandom()
    {
        var scan = AssemblyScan.Run(typeof(SimAssemblyTests).Assembly, AssemblyScan.RandomTypes, reportFloatingPointInstructions: false);
        var prefix = typeof(FloatingPointSamples).FullName + ".";

        Assert.Contains(scan.Findings, finding => finding.StartsWith(prefix + nameof(FloatingPointSamples.RandomGenerator) + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Findings, finding => finding.StartsWith(prefix + nameof(FloatingPointSamples.ArithmeticOnly) + ":", StringComparison.Ordinal));
    }
}

/// <summary>Code that breaks the determinism rules on purpose, for the scan to find. Nothing calls it.</summary>
internal static class FloatingPointSamples
{
    internal static double Field = 1;

    internal static float Property => 0;

    internal static int Parameter(float value) => value > 0 ? 1 : 0;

    internal static double ReturnValue() => 0;

    internal static bool Local(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    /// <summary>No signature, local or call mentions a floating-point type here. Only the instructions do.</summary>
    internal static int ArithmeticOnly(int value) => (int)(value * 0.5);

    /// <summary>No floating-point instruction here. Only the methods it calls take and return one.</summary>
    internal static int CallOnly(string text) => Math.Sign(double.Parse(text, CultureInfo.InvariantCulture));

    internal static int GenericType() => new List<float>().Count;

    internal static int GenericMethod() => Array.Empty<double>().Length;

    internal static int ArrayOfFloats(int length) => new float[length].Length;

    internal static int DecimalNumber(int value) => decimal.ToInt32(value);

    internal static Type TypeOf() => typeof(Half);

    internal static int Lambda(int[] values) => values.Count(value => value * 0.5 > 1);

    internal static int RandomGenerator() => new Random(1).Next();

    internal static long WholeNumbersOnly(int[] values)
    {
        var total = Math.BigMul(values.Length, 3L);
        foreach (var value in values.Where(value => value % 2 == 0).OrderBy(value => value))
        {
            total += Math.Max(value, 1) * (long)Math.Abs(value);
        }

        var text = $"{total} from {values.Length} values";
        return (long)(total / new List<long> { 7 }[0]) + text.Length;
    }
}

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
