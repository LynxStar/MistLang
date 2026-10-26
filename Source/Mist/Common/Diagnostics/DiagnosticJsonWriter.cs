using System.Text.Json;

namespace Mist.Common.Diagnostics;

public static partial class DiagnosticJsonWriter
{
    public static byte[] Serialize(DiagnosticDocument document)
    {
        using var stream = new MemoryStream();
        using(var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", "1.0");
            writer.WriteString("compiler", document.Compiler);
            writer.WriteString("command", document.Command);
            writer.WriteString("project", document.Project);
            writer.WriteString("outcome", document.Outcome);
            var counts = DiagnosticCounts.From(document.Diagnostics, document.Minimum);
            writer.WriteStartObject("counts");
            writer.WriteNumber("admitted", counts.Admitted);
            writer.WriteNumber("errors", counts.Errors);
            writer.WriteNumber("warnings", counts.Warnings);
            writer.WriteNumber("info", counts.Info);
            writer.WriteNumber("hidden", counts.Hidden);
            writer.WriteEndObject();
            writer.WriteStartArray("diagnostics");
            foreach(var diagnostic in document.Diagnostics)
            {
                WriteDiagnostic(writer, diagnostic, document.Minimum);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("native");
            foreach(var native in document.Native)
            {
                writer.WriteStartObject();
                writer.WriteString("provider", native.Provider);
                writer.WriteString("code", native.Code);
                writer.WriteString("severity", native.Severity);
                writer.WriteString("message", native.Message);
                writer.WritePropertyName("location");
                WriteLocation(writer, native.Location);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteDiagnostic(Utf8JsonWriter writer, Diagnostic diagnostic, DiagnosticSeverity minimum)
    {
        writer.WriteStartObject();
        writer.WriteNumber("ordinal", diagnostic.Ordinal);
        writer.WriteString("code", diagnostic.Number.Value);
        writer.WriteString("title", diagnostic.Descriptor.Title);
        writer.WriteString("category", diagnostic.Descriptor.Category.Token());
        writer.WriteString("policy", Token(diagnostic.Descriptor.Policy));
        writer.WriteStartObject("severity");
        writer.WriteString("default", Token(diagnostic.DefaultSeverity));
        writer.WriteString("effective", Token(diagnostic.Severity));
        writer.WriteString("reason", diagnostic.SeverityReason);
        writer.WriteEndObject();
        writer.WriteString("message", diagnostic.Message);
        writer.WriteStartObject("arguments");
        for(var i = 0; i < diagnostic.Descriptor.Slots.Length && i < diagnostic.Arguments.Length; i++)
        {
            writer.WritePropertyName(diagnostic.Descriptor.Slots[i].Name);
            WriteSlot(writer, diagnostic.Arguments[i]);
        }

        writer.WriteEndObject();
        writer.WritePropertyName("location");
        WriteLocation(writer, diagnostic.Location);
        writer.WriteStartArray("related");
        foreach(var related in diagnostic.Related)
        {
            writer.WriteStartObject();
            writer.WriteString("role", Token(related.Role));
            writer.WritePropertyName("location");
            WriteLocation(writer, related.Location);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("frames");
        foreach(var frame in diagnostic.Frames)
        {
            WriteFrame(writer, frame);
        }

        writer.WriteEndArray();
        if(diagnostic.Cause is { } cause)
        {
            writer.WriteNumber("cause", cause);
        }
        else
        {
            writer.WriteNull("cause");
        }

        writer.WritePropertyName("native");
        if(diagnostic.Native is { } native)
        {
            writer.WriteStartObject();
            writer.WriteString("provider", native.Provider);
            writer.WriteString("code", native.Code);
            writer.WriteString("severity", Token(native.NativeSeverity));
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteString("stage", Token(diagnostic.Stage));
        writer.WriteBoolean("visible", DiagnosticCounts.IsVisible(diagnostic, minimum));
        writer.WriteEndObject();
    }

    private static string Token<T>(T value) where T : Enum
    {
        return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    }

    private static void WriteSlot(Utf8JsonWriter writer, SlotValue value)
    {
        switch(value)
        {
            case NameValue name: writer.WriteStringValue(name.Name); break;
            case PathValue path: writer.WriteStringValue(path.Path); break;
            case TextValue text: writer.WriteStringValue(text.Text); break;
            case EnumValue token: writer.WriteStringValue(token.Token); break;
            case QuantityValue quantity: writer.WriteNumberValue(quantity.Value); break;
            case BooleanValue boolean: writer.WriteBooleanValue(boolean.Value); break;
            case TypeValue type: WriteIdentity(writer, type.Spelling, type.Identity); break;
            case SymbolValue symbol: WriteIdentity(writer, symbol.Spelling, symbol.Identity); break;
            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach(var item in sequence.Items)
                {
                    WriteSlot(writer, item);
                }

                writer.WriteEndArray();
                break;
            case OptionalValue { Value: { } present }: WriteSlot(writer, present); break;
            case OptionalValue: writer.WriteNullValue(); break;
        }
    }

    private static void WriteIdentity(Utf8JsonWriter writer, string spelling, string identity)
    {
        writer.WriteStartObject();
        writer.WriteString("spelling", spelling);
        writer.WriteString("identity", identity);
        writer.WriteEndObject();
    }
}