using Buzz.MachineInterface;
using BuzzGUI.Common;
using BuzzGUI.Common.InterfaceExtensions;
using BuzzGUI.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.TextFormatting;

namespace WDE.EasyRec
{
    enum EasyRecState
    {
        Stop = 0,
        Record
    };

    [MachineDecl(Name = "WDE EasyRec", ShortName = "EasyRec", Author = "WDE", MaxTracks = 1)]
    public class EasyRec : IBuzzMachine, INotifyPropertyChanged
    {
        IBuzzMachineHost host;
        SampleData sampleData;
        int currentSlot = 0;

        private object lockObj = new object();

        public EasyRec(IBuzzMachineHost host)
        {
            this.host = host;
            sampleData = new SampleData();
        }

        private int GetMaxLatency()
        {
            int latency = 0;

            if (Global.EngineSettings.MachineDelayCompensation)
            {
                foreach (IMachine mac in this.host.Machine.Graph.Buzz.Song.Machines)
                {
                    if (mac.IsActive)
                    {
                        if (mac.OverrideLatency != -1)
                        {
                            if (mac.OverrideLatency > latency)
                            {
                                latency = mac.OverrideLatency;
                            }
                        }
                        else
                        {
                            if (mac.Latency > latency)
                            {
                                latency = mac.Latency;
                            }
                        }
                    }
                }
            }

            return latency;
        }

        int freezerState = (int)EasyRecState.Stop;

        [ParameterDecl(MaxValue = 1, DefValue = 0, ValueDescriptions = new String[] { "Stop", "Record" })]
        public int Mode
        {
            get { return freezerState; }
            set
            {
                freezerState = (int)value;
                switch ((int)value)
                {
                    case (int)EasyRecState.Record:

                        lock (lockObj)
                        {
                            sampleData.NewRecord();

                            host.MasterInfo.PosInTick = 0;
                            host.MasterInfo.PosInGroove = 0;
                            host.SubTickInfo.PosInSubTick = 0;
                            host.SubTickInfo.CurrentSubTick = 0;

                            TickZeroReached = false;
                        }

                        break;
                    case (int)EasyRecState.Stop:

                        TickZeroReached = false;

                        if (this.host.Machine != null)
                        {
                            string prefix = MachineState.Text != "" ? MachineState.Text + " " : "";
                            waveName = prefix + "" /* this.host.Machine.Name + " " */ + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture); // <-- Force colon

                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                Mouse.OverrideCursor = Cursors.Wait;
                                if (!sampleData.IsEmpty())
                                {
                                    try
                                    {
                                        sampleData.TrimBuffer();
                                        int latency = GetMaxLatency();
                                        int targetSlot = Overwrite ? WavetableSlot - 1 : FindNextAvailableIndex(WavetableSlot - 1);
                                        if (targetSlot != -1)
                                        {
                                            float[] buffer = sampleData.GetBuffer(0);
                                            sampleData.Init();
                                            saveToWavetable(targetSlot, ref buffer);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        // Do nothing
                                        MessageBox.Show(this.host.Machine.Name + ": Could not allocate wave in Wavetable.");
                                    }

                                }
                                Mouse.OverrideCursor = null;
                            }));
                        }
                        break;
                }
            }
        }

        [ParameterDecl(DefValue = true, ValueDescriptions = new String[] { "Disabled", "Enabled" })]
        public bool AutoStop
        {
            get;
            set;
        }

        [ParameterDecl(MinValue = 1, MaxValue = 200, DefValue = 1)]
        public int WavetableSlot
        {
            get;
            set;
        }

        [ParameterDecl(DefValue = true, ValueDescriptions = new String[] { "No", "Yes" } )]
        public bool Overwrite
        {
            get;
            set;
        }

        string waveName;

        public void Stop()
        {
            if (freezerState == (int)EasyRecState.Record && AutoStop)
            {
                IParameter param = host.Machine.GetParameter("Mode");
                if (param != null)
                    param.SetValue(0, (int)EasyRecState.Stop);
            }
        }

        private int FindNextAvailableIndex(int myWavetableIndex)
        {
            IWavetable wt = Global.Buzz.Song.Wavetable;

            int i;
            for (i = myWavetableIndex; i < 200; i++)
            {
                if (wt.Waves[i] == null)
                    break;
            }

            return (i == 200) || (myWavetableIndex > 199) ? -1 : i;
        }

        bool TickZeroReached = false;

        public bool Work(Sample[] output, Sample[] input, int numsamples, WorkModes mode)
        {

            if (mode == WorkModes.WM_NOIO)
                return false;
            if (mode == WorkModes.WM_READ)                        // <thru>
                return true;

            bool ret = true; //Create sound

            lock (lockObj)
            {
                switch (freezerState)
                {
                    case (int)EasyRecState.Record:
                        {
                            if (Global.Buzz.Playing || Global.Buzz.Recording)
                            {
                                if (host.MasterInfo.PosInTick == 0) // Start only recording when tick reaches 0 first.
                                    TickZeroReached = true;

                                if (mode == WorkModes.WM_WRITE) // No audio or silence in input
                                {
                                    if (TickZeroReached) // Now we know that tick has started from beginning, thus we can add silence.
                                        sampleData.AppendSilence(numsamples);

                                    for (int i = 0; i < numsamples; i++)
                                    {
                                        output[i] = new Sample(0.0f, 0.0f);
                                    }
                                    return true;                                    
                                }
                                else
                                {
                                    if (TickZeroReached)
                                        sampleData.Append(input, numsamples);
                                }
                            }
                        }
                        break;

                    case (int)EasyRecState.Stop:
                        {
                            if (mode == WorkModes.WM_WRITE)
                                return false;
                        }
                        break;
                }

                if (mode == WorkModes.WM_WRITE && !(Global.Buzz.Playing || Global.Buzz.Recording))
                {
                    return false;
                }

                for (int i = 0; i < numsamples; i++)
                {
                    output[i] = input[i];
                }
            }
            return ret;
        }


        // actual machine ends here. the stuff below demonstrates some other features of the api.
        public IEnumerable<IMenuItem> Commands
        {
            get
            {
                yield return new MenuItemVM()
                {
                    Text = "Help",
                    Command = new SimpleCommand()
                    {
                        CanExecuteDelegate = p => true,
                        ExecuteDelegate = p => MessageBox.Show("Modes\n\nRecord: Start recording input audio when song is played.\nStop: Stop recording and copy audio to wavetable.")
                    }
                };

                yield return new MenuItemVM()
                {
                    Text = "About...",
                    Command = new SimpleCommand()
                    {
                        CanExecuteDelegate = p => true,
                        ExecuteDelegate = p => MessageBox.Show("Version 1.0.3 (C) 2020 WDE")
                    }
                };
            }
        }

        public void saveToWavetable(int slot, ref float[] buffer)
        {
            var wt = host.Machine.Graph.Buzz.Song.Wavetable;
            WaveFormat wf;
            wf = WaveFormat.Float32;

            //string waveName = "EasyRec " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture); // <-- Force colon

            // write to wavetable
            int rootnote = BuzzNote.FromMIDINote(48);
            wt.AllocateWave(slot,
                                "",
                                waveName,
                                (int)(buffer.Length / 2), // Stereo --> divide by 2
                                wf,
                                true,
                                rootnote,
                                false,
                                true);
            IWaveLayer layer = wt.Waves[slot].Layers.Last();
            layer.SampleRate = Global.Buzz.SelectedAudioDriverSampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = buffer[i] / 32768.0f;
            }

            layer.SetDataAsFloat(buffer, 0, 2, 0, 0, (int)buffer.Length / 2); // Left
            layer.SetDataAsFloat(buffer, 1, 2, 1, 0, (int)buffer.Length / 2); // Right
            layer.LoopStart = 0;
            layer.LoopEnd = buffer.Length / 2;

            layer.InvalidateData();
        }

        internal void slotUpdated(int decAgain)
        {
            currentSlot = decAgain;
        }

        public class State : INotifyPropertyChanged
        {
            public State() { text = "EasyRec"; }  // NOTE: parameterless constructor is required by the xml serializer

            string text;
            public string Text
            {
                get { return text; }
                set
                {
                    text = value;
                    text = Path.GetInvalidFileNameChars().Aggregate(text, (f, c) => f.Replace(c, '_'));
                    if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Text"));
                    // NOTE: the INotifyPropertyChanged stuff is only used for data binding in the GUI in this demo. it is not required by the serializer.
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        State machineState = new State();

        public event PropertyChangedEventHandler PropertyChanged;

        public State MachineState           // a property called 'MachineState' gets automatically saved in songs and presets
        {
            get { return machineState; }
            set
            {
                machineState = value;
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("MachineState"));
            }
        }
    }

    public class MachineGUIFactory : IMachineGUIFactory { public IMachineGUI CreateGUI(IMachineGUIHost host) { return new EasyRecGUI(); } }
    public class EasyRecGUI : UserControl, IMachineGUI
    {
        IMachine machine;
        EasyRec EasyRecMachine;
        TextBox tb;

        public IMachine Machine
        {
            get { return machine; }
            set
            {
                if (machine != null)
                {
                    BindingOperations.ClearBinding(tb, TextBox.TextProperty);
                }

                machine = value;

                if (machine != null)
                {
                    EasyRecMachine = (EasyRec)machine.ManagedMachine;
                    tb.SetBinding(TextBox.TextProperty, new Binding("MachineState.Text") { Source = EasyRecMachine, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                }
            }
        }

        public EasyRecGUI()
        {
            tb = new TextBox() { Margin = new Thickness(0, 0, 0, 4), AllowDrop = true };
            Label label = new Label() { Margin = new Thickness(0, 0, 0, 4), Content = "Wave Name Prefix:" };

            var sp = new StackPanel();
            sp.Children.Add(label);
            sp.Children.Add(tb);
            this.Content = sp;

            tb.PreviewDragEnter += (sender, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            tb.PreviewDragOver += (sender, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            tb.Drop += (sender, e) => { tb.Text = "drop"; e.Handled = true; };
        }
    }
}
