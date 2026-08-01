using BenchmarkDotNet.Running;

namespace Quillwright.Benchmarks;

/// <summary>Entry point for the benchmark suite.</summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
