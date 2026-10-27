using System.Text.Json;
using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

public static partial class DiagnosticJsonWriter
{
    private static void WriteLocation(Utf8JsonWriter writer, Location location)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", location switch
        {
            SourceRange => "sourceRange",
            SourcePoint => "sourcePoint",
            FileOnly => "fileOnly",
            ProjectLocation => "project",
            ExternalLocation => "external",
            GeneratedLocation => "generated",
            _ => "absent",
        });
        if(location.Path is { } path)
        {
            writer.WriteString("path", path);
        }

        switch(location)
        {
            case SourceRange range:
                WriteLines(writer, range.Lines!.Value);
                writer.WriteNumber("offset", range.Offset);
                writer.WriteNumber("length", range.Length);
                break;
            case SourcePoint point:
                WriteLines(writer, point.Lines!.Value);
                writer.WriteNumber("offset", point.Offset);
                break;
            case ProjectLocation project:
                writer.WritePropertyName("keySpan");
                WriteNullableLocation(writer, project.KeySpan);
                break;
            case ExternalLocation external:
                writer.WriteString("assembly", external.Assembly);
                writer.WriteString("symbol", external.Symbol);
                break;
            case GeneratedLocation generated:
                WriteLines(writer, generated.Native);
                writer.WritePropertyName("mapped");
                WriteNullableLocation(writer, generated.Mapped);
                break;
            case Absent absent:
                writer.WriteString("reason", Token(absent.Reason));
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableLocation(Utf8JsonWriter writer, Location? location)
    {
        if(location is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            WriteLocation(writer, location);
        }
    }

    private static void WriteLines(Utf8JsonWriter writer, LinePositionSpan lines)
    {
        writer.WriteNumber("line", lines.Start.Line);
        writer.WriteNumber("column", lines.Start.Column);
        writer.WriteNumber("endLine", lines.End.Line);
        writer.WriteNumber("endColumn", lines.End.Column);
    }

    private static void WriteFrame(Utf8JsonWriter writer, Frame frame)
    {
        writer.WriteStartObject();
        switch(frame)
        {
            case LoweringFrame lowering:
                writer.WriteString("kind", "lowering");
                writer.WriteString("reason", lowering.Reason);
                break;
            case SynthesisFrame synthesis:
                writer.WriteString("kind", "synthesis");
                writer.WriteString("reason", synthesis.Reason);
                break;
            case OperationFrame operation:
                writer.WriteString("kind", "operation");
                writer.WriteString("project", operation.Project);
                writer.WriteStartArray("referenceChain");
                foreach(var reference in operation.ReferenceChain)
                {
                    writer.WriteStringValue(reference);
                }

                writer.WriteEndArray();
                writer.WriteString("package", operation.Package);
                break;
            case WithholdingFrame withheld:
                writer.WriteString("kind", "withholding");
                writer.WritePropertyName("member");
                WriteSlot(writer, withheld.Member);
                writer.WriteString("reason", withheld.Reason.Token);
                writer.WritePropertyName("offending");
                if(withheld.Offending is { } offending)
                {
                    WriteSlot(writer, offending);
                }
                else
                {
                    writer.WriteNullValue();
                }

                break;
            case TruncatedFrame truncated:
                writer.WriteString("kind", "truncated");
                writer.WriteNumber("depth", truncated.Depth);
                writer.WriteBoolean("cyclic", truncated.Cyclic);
                break;
        }

        writer.WriteEndObject();
    }
}