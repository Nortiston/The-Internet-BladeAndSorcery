using System;
using TheInternet;
static class Program
{
    static void Check(bool condition) { if (!condition) throw new Exception("Trigger regression"); }
    static void Main()
    {
        var gate = new TriggerPressGate();
        Check(gate.Begin());
        // Duplicate Down on the same hold cannot arm a second activation.
        for (int i = 0; i < 300; i++) Check(!gate.Begin());
        Check(gate.Complete());
        // Late Up/Click notifications and a Down before physical release do nothing.
        Check(!gate.Complete()); Check(!gate.Complete()); Check(!gate.Begin());
        gate.Release();
        Check(gate.Begin()); Check(gate.Complete()); gate.Release();
        // Leaving a control during a hold cancels it, and re-entry cannot re-arm it.
        Check(gate.Begin()); gate.Cancel(); Check(!gate.Begin()); Check(!gate.Complete()); gate.Release();
        Check(gate.Begin()); Check(gate.Complete()); gate.Release();
        gate.Reset(); Check(!gate.Active && !gate.Latched);
        var pcm = new StreamingPcmBuffer(4);
        pcm.Write(new float[] { .1f, .2f, .3f, .4f, .5f, .6f });
        Check(pcm.Available == 4);
        var output = new float[6]; pcm.Read(output);
        Check(output[0] == .3f && output[3] == .6f && output[4] == 0 && output[5] == 0);
        pcm.Write(new float[] { float.NaN, float.PositiveInfinity, 2, -2 }); pcm.Read(output);
        Check(output[0] == 0 && output[1] == 0 && output[2] == 1 && output[3] == -1);
        pcm.Write(new float[] { .5f }); pcm.Clear(); Check(pcm.Available == 0);
        var jitter=new StreamingPcmBuffer(100,24);
        jitter.Write(new float[8]);var chunk=new float[8];jitter.Read(chunk);Check(jitter.Available==8);
        jitter.Write(new float[16]);jitter.Read(chunk);Check(jitter.Available==16);
        jitter.Read(new float[20]);Check(jitter.Underruns==1);
        jitter.Write(new float[8]);jitter.Read(chunk);Check(jitter.Available==8);
        jitter.Write(new float[16]);jitter.Read(chunk);Check(jitter.Available==16);
        var sustained=new StreamingPcmBuffer(96000,24576);
        var packet=new float[4096];for(int i=0;i<packet.Length;i++)packet[i]=.2f;
        var callbacks=new float[1024];var pending=new System.Collections.Generic.List<int>();
        for(int tick=0;tick<12000;tick++){
          if(tick%4==0)pending.Add(tick+(tick/4)%4);
          for(int j=pending.Count-1;j>=0;j--)if(pending[j]<=tick){sustained.Write(packet);pending.RemoveAt(j);}
          sustained.Read(callbacks);
        }
        Check(sustained.Underruns==0);
        Console.WriteLine("PASS: one activation per trigger cycle, duplicate events suppressed, exit cancels, next click works.");
        Console.WriteLine("PASS: bounded PCM buffering, overflow recovery, silence on underrun, sample sanitizing and clearing.");
        Console.WriteLine("PASS: prebuffer/refill hysteresis and continuous playback under packet-arrival jitter.");
    }
}
