
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Godot;

public partial class arrayspantimer : Node
{
    public override void _Ready()
    {
        List<int> numbers = new List<int>(100_000_000);

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        foreach (var i in CollectionsMarshal.AsSpan(numbers))
        {
            
        }
        stopwatch.Stop();
        GD.Print($"Time To Itterate int.MAX AsSpan: {stopwatch.Elapsed.TotalMilliseconds:F4}" );
    }
}
