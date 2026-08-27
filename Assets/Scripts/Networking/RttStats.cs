using System.Diagnostics;

/// <summary>
///   EstimatedRTT    = (1-a) * EstimatedRTT + a * SampleRTT
///   DevRTT          = (1-b) * DevRTT       + b * |SampleRTT - EstimatedRTT|
///   TimeoutInterval = EstimatedRTT + K * DevRTT
/// 
/// </summary>
public class RttStats
{
    public float EstimatedRtt { get; private set; }
    public float DevRtt { get; private set; }
    public float LastSampleRtt { get; private set; }
    public float LastSampleTime { get; private set; }
    public bool HasEstimate { get; private set; }
    public int ProbesSent { get; private set; }
    public int RepliesReceived { get; private set; }


    private const int OutstandingSlots = 4;
    private readonly long[] outstanding = new long[OutstandingSlots];
    private int outstandingHead;

    private const int WindowSlots = 32;
    private readonly float[] sampleTimes = new float[WindowSlots];
    private int sampleHead;

    public void NoteProbeSent(long stamp)
    {
        outstanding[outstandingHead] = stamp;
        outstandingHead = (outstandingHead + 1) % OutstandingSlots;
        ProbesSent++;
    }

    public bool TryAcceptReply(long echoedStamp, float now, float alpha, float beta, float simulatedExtraMs)
    {
        int slot = -1;

        for (int i = 0; i < OutstandingSlots; i++)
        {
            if (outstanding[i] == echoedStamp && echoedStamp != 0)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
            return false;

        outstanding[slot] = 0;

        long elapsed = Stopwatch.GetTimestamp() - echoedStamp;
        if (elapsed < 0)
            return false;

        float sampleRtt = (float)(elapsed * 1000.0 / Stopwatch.Frequency) + simulatedExtraMs;

        AddSample(sampleRtt, now, alpha, beta);
        RepliesReceived++;
        return true;
    }

    private void AddSample(float sampleRtt, float now, float alpha, float beta)
    {
        if (!HasEstimate)
        {
            EstimatedRtt = sampleRtt;
            DevRtt = sampleRtt / 2f;
            HasEstimate = true;
        }
        else
        {

            DevRtt = (1f - beta) * DevRtt + beta * UnityEngine.Mathf.Abs(sampleRtt - EstimatedRtt);
            EstimatedRtt = (1f - alpha) * EstimatedRtt + alpha * sampleRtt;
        }

        LastSampleRtt = sampleRtt;
        LastSampleTime = now;

        sampleTimes[sampleHead] = now;
        sampleHead = (sampleHead + 1) % WindowSlots;
    }

    public float TimeoutInterval(float deviationWeight)
    {
        return EstimatedRtt + deviationWeight * DevRtt;
    }

    public int SamplesInWindow(float now, float window)
    {
        int count = 0;

        for (int i = 0; i < WindowSlots; i++)
        {
            if (sampleTimes[i] > 0f && now - sampleTimes[i] <= window)
                count++;
        }

        return count;
    }

    public bool IsUsable(float now, float window, int minSamples)
    {
        return HasEstimate && SamplesInWindow(now, window) >= minSamples;
    }

    public void Reset()
    {
        EstimatedRtt = 0f;
        DevRtt = 0f;
        LastSampleRtt = 0f;
        LastSampleTime = 0f;
        HasEstimate = false;
        ProbesSent = 0;
        RepliesReceived = 0;
        outstandingHead = 0;
        sampleHead = 0;

        for (int i = 0; i < OutstandingSlots; i++) outstanding[i] = 0;
        for (int i = 0; i < WindowSlots; i++) sampleTimes[i] = 0f;
    }
}
