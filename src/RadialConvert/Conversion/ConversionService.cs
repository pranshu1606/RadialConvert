using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

internal sealed record ConversionResult(string InputPath, string? OutputPath, Exception? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>Runs conversions one at a time on a background STA thread (Office COM requires STA).</summary>
internal sealed class ConversionService
{
    readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public async Task<IReadOnlyList<ConversionResult>> ConvertAsync(IReadOnlyList<string> inputs, ConversionOption option)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            return await RunOnStaThread(() =>
            {
                var results = new List<ConversionResult>();
                foreach (string input in inputs)
                    results.Add(ConvertOne(input, option));
                return results;
            });
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public static ConversionResult ConvertOne(string input, ConversionOption option)
    {
        string output = OutputPaths.Next(input, option);
        try
        {
            Log.Info($"Converting '{input}' -> '{output}' with {option.Converter.Name}");
            option.Converter.Convert(input, output, option);
            Log.Info("Done");
            return new ConversionResult(input, output, null);
        }
        catch (Exception ex)
        {
            Log.Error($"Converting '{input}' to {option.Label} failed", ex);
            return new ConversionResult(input, null, ex);
        }
    }

    static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "Conversion" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
