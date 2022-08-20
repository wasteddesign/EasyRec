using Buzz.MachineInterface;
using System.Collections.Generic;

namespace WDE.EasyRec
{

    public class SampleData
    {
        private static int BUFFER_SIZE = 44100 * 2 * 4;
        private Dictionary<int, float[]> sampleBuffers;
        private int totalSize; // In stereo samples, 1 == L + R

        public SampleData()
        {
            Init();
        }

        public void Init()
        {
            sampleBuffers = new Dictionary<int, float[]>();
            totalSize = 0;
        }

        public float[] GetBuffer(int latency)
        {
            float[] retBuffer = new float[(totalSize - latency) * 2];

            for (int i = 0; i < totalSize - latency; i++)
            {
                Sample sample = GetSample(i + latency);
                retBuffer[i * 2] = sample.L;
                retBuffer[i * 2 + 1] = sample.R;
            }

            return retBuffer;
        }

        private Sample GetSample(int pos)
        {
            Sample ret = new Sample();
            pos *= 2;
            int block = (int)pos / BUFFER_SIZE;
            int posInBlock = pos % BUFFER_SIZE;

            if (sampleBuffers.ContainsKey(block))
            {
                float[] buffer = sampleBuffers[block];
                ret.L = buffer[posInBlock];
                ret.R = buffer[posInBlock + 1];

            }
            return ret;
        }

        public void TrimBuffer()
        {
        }

        public void AddSample(Sample sample, int pos)
        {
            if (pos >= totalSize)
                totalSize = pos + 1;

            pos *= 2;
            int block = (int)pos / BUFFER_SIZE;
            int posInBlock = pos % BUFFER_SIZE;

            if (!sampleBuffers.ContainsKey(block))
            {
                sampleBuffers.Add(block, new float[BUFFER_SIZE]);                
            }

            float[] buffer = sampleBuffers[block];
            buffer[posInBlock] = sample.L;
            buffer[posInBlock + 1] = sample.R;
        }

        internal void NewRecord()
        {
            Init();
        }

        internal void Append(Sample[] input, int numsamples)
        {
            for (int i = 0; i < numsamples; i++)
            {
                this.AddSample(input[i], totalSize);
            }
        }

        public void AddSamples(Sample[] input, int numsamples, int pos)
        {
            for (int i = 0; i < numsamples; i++)
            {
                this.AddSample(input[i], pos + i);
            }
        }

        public bool IsEmpty()
        {
            return sampleBuffers.Count == 0;
        }

        internal void AppendSilence(int numsamples)
        {

            for (int i = 0; i < numsamples; i++)
            {
                this.AddSample(new Sample(), totalSize);
            }
        }

        public void AddSilence(int numsamples, int pos)
        {
            for (int i = 0; i < numsamples; i++)
            {
                this.AddSample(new Sample(), pos + i);
            }
        }
    }
}
