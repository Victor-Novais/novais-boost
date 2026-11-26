using System.Buffers.Binary;
using System.Text;

namespace NovaisBoost.Core;

public sealed record PingStats(int Sent, int Received, double AverageMs, double MinMs, double MaxMs, double JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : 100d * (Sent - Received) / Sent;
}

public sealed record DnsResult(string Name, string Server, double MedianMs, int Failures);

public static class NetworkAnalysis
{
    /// <summary>Jitter is the mean absolute difference between consecutive replies. Null entries are lost packets.</summary>
    public static PingStats Ping(IReadOnlyList<double?> roundTripsMs)
    {
        var replies = roundTripsMs.OfType<double>().ToArray();
        if (replies.Length == 0)
            return new PingStats(roundTripsMs.Count, 0, 0, 0, 0, 0);
        var jitter = replies.Length < 2 ? 0 : replies.Zip(replies.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new PingStats(roundTripsMs.Count, replies.Length, replies.Average(), replies.Min(), replies.Max(), jitter);
    }

    /// <summary>Grades the latency increase under load, like common bufferbloat tests.</summary>
    public static string BufferbloatGrade(double idleMs, double loadedMs) => (loadedMs - idleMs) switch
    {
        < 5 => "A+",
        < 30 => "A",
        < 60 => "B",
        < 200 => "C",
        < 400 => "D",
        _ => "F"
    };

    public static IReadOnlyList<Finding> Evaluate(PingStats ping, string? grade)
    {
        var findings = new List<Finding>();
        if (ping.LossPercent > 0)
            findings.Add(new Finding("packet-loss", ping.LossPercent >= 2 ? Severity.Critical : Severity.Warning,
                $"Perda de pacotes: {ping.LossPercent:0.#}%", "Pacotes perdidos viram teleporte e tiros que não registram.",
                "Use cabo, troque o cabo/porta e fale com o provedor se continuar."));
        if (ping.JitterMs >= 5)
            findings.Add(new Finding("jitter", ping.JitterMs >= 15 ? Severity.Critical : Severity.Warning,
                $"Jitter alto: {ping.JitterMs:0.#} ms", "O ping varia muito entre pacotes.", "Use cabo e evite downloads durante o jogo."));
        if (grade is "C" or "D" or "F")
            findings.Add(new Finding("bufferbloat", grade == "C" ? Severity.Warning : Severity.Critical,
                $"Bufferbloat nota {grade}", "O ping dispara quando alguém usa a internet (download, stream, atualização).",
                "Ative SQM/QoS (Smart Queue) no roteador ou limite a banda de downloads."));
        return findings;
    }

    // Minimal DNS (RFC 1035) A-record query, used to time resolvers without the OS cache.
    public static byte[] BuildQuery(ushort id, string name)
    {
        var buffer = new List<byte>(64);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // one question
        buffer.AddRange(header.ToArray());
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException($"Nome DNS inválido: {name}.", nameof(name));
            buffer.Add((byte)bytes.Length);
            buffer.AddRange(bytes);
        }
        buffer.AddRange([0, 0, 1, 0, 1]); // root, type A, class IN
        return buffer.ToArray();
    }

    /// <summary>True when the reply matches the query id and resolved without error.</summary>
    public static bool IsValidReply(ReadOnlySpan<byte> reply, ushort id) =>
        reply.Length >= 12 && BinaryPrimitives.ReadUInt16BigEndian(reply) == id &&
        (reply[2] & 0x80) != 0 && (reply[3] & 0x0F) == 0;
}
