/* 
 *	Copyright (C) 2005-2006 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */
//#define FORM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using DirectShowLib;
using DirectShowLib.SBE;
using DirectShowLib.BDA;
using TvLibrary.Interfaces;
using TvLibrary.Interfaces.Analyzer;
using TvLibrary.Interfaces.TsFileSink;
using TvLibrary.Epg;
using TvLibrary.Channels;
using TvLibrary.Implementations.DVB.Structures;
using TvLibrary.Teletext;
using TvLibrary.Helper;

namespace TvLibrary.Implementations.DVB
{
  /// <summary>
  /// Implementation of <see cref="T:TvLibrary.Interfaces.ITVCard"/> which handles the SkyStar 2 DVB-S card
  /// </summary>
  public class TvCardDvbSS2 : ITVCard, IDisposable, ITeletextCallBack, IPMTCallback
  {
    #region imports

    [ComImport, Guid("fc50bed6-fe38-42d3-b831-771690091a6e")]
    class MpTsAnalyzer { }

    #endregion



    #region variables
    DsDevice _tunerDevice;
    string _recordingFileName;
    GraphState _graphState;
    IChannel _currentChannel;
    IFilterGraph2 _graphBuilder;
    DsROTEntry _rotEntry;
    ICaptureGraphBuilder2 _capBuilder;
    IBaseFilter _infTeeMain = null;
    IBaseFilter _filterB2C2Adapter;
    IBaseFilter _filterNetworkProvider = null;
    IBaseFilter _filterMpeg2DemuxTif = null;

    IBaseFilter _filterTIF = null;
    IBaseFilter _filterSectionsAndTables = null;
    IBaseFilter _filterTsAnalyzer;



    DVBAudioStream _currentAudioStream;
    DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 _interfaceB2C2DataCtrl;
    DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2 _interfaceB2C2TunerCtrl;


    DVBTeletext _teletextDecoder;
    bool _hasTeletext = false;
    ITsEpgScanner _interfaceEpgGrabber;
    ITsChannelScan _interfaceChannelScan;
    ITsPmtGrabber _interfacePmtGrabber;

    bool _graphRunning;
    bool _epgGrabbing;
    int _managedThreadId;
    bool _tunerLocked;
    int _signalQuality;
    int _signalLevel;
    DateTime _lastSignalUpdate;
    bool _startTimeShifting = false;
    BaseEpgGrabber _epgGrabberCallback;

#if FORM
     bool _newPMT = false;
    System.Windows.Forms.Timer _pmtTimer = new System.Windows.Forms.Timer();
#else
    System.Timers.Timer _pmtTimer = new System.Timers.Timer();
#endif
    int _pmtVersion;
    ChannelInfo _channelInfo = new ChannelInfo();
    string _timeshiftFileName;
    bool _isScanning = false;
    DateTime _dateTimeShiftStarted = DateTime.MinValue;
    DateTime _dateRecordingStarted = DateTime.MinValue;

    #region teletext
    bool _grabTeletext = false;
    TSHelperTools.TSHeader _packetHeader;
    TSHelperTools _tsHelper = new TSHelperTools();
    #endregion

    #endregion

    #region enums
    enum GraphState
    {
      Idle,
      Created,
      TimeShifting,
      Recording
    }
    #endregion

    #region enums

    enum TunerType
    {
      ttSat = 0,
      ttCable = 1,
      ttTerrestrial = 2,
      ttATSC = 3,
      ttUnknown = -1
    }
    enum eModulationTAG
    {
      QAM_4 = 2,
      QAM_16,
      QAM_32,
      QAM_64,
      QAM_128,
      QAM_256,
      MODE_UNKNOWN = -1
    };
    enum GuardIntervalType
    {
      Interval_1_32 = 0,
      Interval_1_16,
      Interval_1_8,
      Interval_1_4,
      Interval_Auto
    };
    enum BandWidthType
    {
      MHz_6 = 6,
      MHz_7 = 7,
      MHz_8 = 8,
    };
    enum SS2DisEqcType
    {
      None = 0,
      Simple_A,
      Simple_B,
      Level_1_A_A,
      Level_1_B_A,
      Level_1_A_B,
      Level_1_B_B
    };
    enum FecType
    {
      Fec_1_2 = 1,
      Fec_2_3,
      Fec_3_4,
      Fec_5_6,
      Fec_7_8,
      Fec_Auto
    }

    enum LNBSelectionType
    {
      Lnb0 = 0,
      Lnb22kHz,
      Lnb33kHz,
      Lnb44kHz,
    } ;

    enum PolarityType
    {
      Horizontal = 0,
      Vertical,
    };

    #endregion

    #region constants
    [ComImport, Guid("BAAC8911-1BA2-4ec2-96BA-6FFE42B62F72")]
    class MPStreamAnalyzer { }

    [ComImport, Guid("BC650178-0DE4-47DF-AF50-BBD9C7AEF5A9")]
    class CyberLinkMuxer { }

    [ComImport, Guid("3E8868CB-5FE8-402C-AA90-CB1AC6AE3240")]
    class CyberLinkDumpFilter { };
    #endregion

    #region imports
    [DllImport("advapi32", CharSet = CharSet.Auto)]
    static extern ulong RegOpenKeyEx(IntPtr key, string subKey, uint ulOptions, uint sam, out IntPtr resultKey);

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    static extern int SetPidToPin(DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 dataCtrl, UInt16 pin, UInt16 pid);

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool DeleteAllPIDs(DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3 dataCtrl, UInt16 pin);

    [DllImport("dvblib.dll", ExactSpelling = true, CharSet = CharSet.Auto, SetLastError = true)]
    static extern int GetSNR(DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2 tunerCtrl, [Out] out int a, [Out] out int b);

    #endregion

    #region ctor
    /// <summary>
    /// Initializes a new instance of the <see cref="TvCardDvbSS2"/> class.
    /// </summary>
    /// <param name="device">The device.</param>
    public TvCardDvbSS2(DsDevice device)
    {
      _tunerDevice = device;
      _graphState = GraphState.Idle;
      _teletextDecoder = new DVBTeletext();
      _packetHeader = new TSHelperTools.TSHeader();
      _tsHelper = new TSHelperTools();
      _pmtTimer.Enabled = false;
      _pmtTimer.Interval = 100;

#if FORM
      _pmtTimer.Tick += new EventHandler(_pmtTimer_ElapsedForm);
#else
      _pmtTimer.Elapsed += new System.Timers.ElapsedEventHandler(_pmtTimer_Elapsed);
#endif
    }
    #endregion

    #region GraphBuilding
    /// <summary>
    /// Checks the thread id.
    /// </summary>
    /// <returns></returns>
    protected bool CheckThreadId()
    {
      return true;
      if (_managedThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId)
      {

        Log.Log.WriteFile("ss2:Invalid thread id!!!");
        return false;
      }
      return true;
    }

    /// <summary>
    /// Builds the graph.
    /// </summary>
    void BuildGraph()
    {
      Log.Log.WriteFile("ss2: build graph");
      if (_graphState != GraphState.Idle)
      {
        Log.Log.WriteFile("ss2: Graph already build");
        throw new TvException("Graph already build");
      }
      DevicesInUse.Instance.Add(_tunerDevice);
      _managedThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
      _graphBuilder = (IFilterGraph2)new FilterGraph();
      _rotEntry = new DsROTEntry(_graphBuilder);
      _capBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
      _capBuilder.SetFiltergraph(_graphBuilder);

      //=========================================================================================================
      // add the skystar 2 specific filters
      //=========================================================================================================
      Log.Log.WriteFile("ss2:CreateGraph() create B2C2 adapter");
      _filterB2C2Adapter = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(DVBSkyStar2Helper.CLSID_B2C2Adapter, false));
      if (_filterB2C2Adapter == null)
      {
        Log.Log.WriteFile("ss2:creategraph() _filterB2C2Adapter not found");
        return;
      }
      Log.Log.WriteFile("ss2:creategraph() add filters to graph");
      int hr = _graphBuilder.AddFilter(_filterB2C2Adapter, "B2C2-Source");
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2: FAILED to add B2C2-Adapter");
        return;
      }
      // get interfaces
      _interfaceB2C2DataCtrl = _filterB2C2Adapter as DVBSkyStar2Helper.IB2C2MPEG2DataCtrl3;
      if (_interfaceB2C2DataCtrl == null)
      {
        Log.Log.WriteFile("ss2: cannot get IB2C2MPEG2DataCtrl3");
        return;
      }
      _interfaceB2C2TunerCtrl = _filterB2C2Adapter as DVBSkyStar2Helper.IB2C2MPEG2TunerCtrl2;
      if (_interfaceB2C2TunerCtrl == null)
      {
        Log.Log.WriteFile("ss2: cannot get IB2C2MPEG2TunerCtrl3");
        return;
      }
      //=========================================================================================================
      // initialize skystar 2 tuner
      //=========================================================================================================
      Log.Log.WriteFile("ss2: Initialize Tuner()");
      hr = _interfaceB2C2TunerCtrl.Initialize();
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2: Tuner initialize failed:0x{0:X}", hr);
        //return;
      }
      // call checklock once, the return value dont matter

      hr = _interfaceB2C2TunerCtrl.CheckLock();

      AddMpeg2DemuxerTif();
      ConnectMainTee();
      ConnectMpeg2DemuxersToMainTee();

      GetVideoAudioPins();

      SendHWPids(new ArrayList());
      _graphState = GraphState.Created;
    }

    /// <summary>
    /// Connects the main tee to the SS2 filter
    /// </summary>
    void ConnectMainTee()
    {
      Log.Log.WriteFile("ss2:ConnectMainTee()");
      int hr = 0;
      IPin pinOut = DsFindPin.ByDirection(_filterB2C2Adapter, PinDirection.Output, 2);
      IPin pinIn = DsFindPin.ByDirection(_infTeeMain, PinDirection.Input, 0);
      if (pinOut == null)
      {
        Log.Log.WriteFile("ss2:unable to find pin 2 of b2c2adapter");
        throw new TvException("unable to find pin 2 of b2c2adapter");
      }
      if (pinIn == null)
      {
        Log.Log.WriteFile("ss2:unable to find pin 0 of _infTeeMain");
        throw new TvException("unable to find pin 0 of _infTeeMain");
      }

      hr = _graphBuilder.Connect(pinOut, pinIn);
      Release.ComObject("b2c2pin2", pinOut);
      Release.ComObject("mpeg2demux pinin", pinIn);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:unable to connect b2c2->_infTeeMain");
        throw new TvException("unable to connect b2c2->_infTeeMain");
      }
    }

    void SetTimeShiftPids()
    {
      if (_channelInfo == null) return;
      if (_channelInfo.pids.Count == 0) return;
      if (_currentChannel == null) return;
      //if (_currentAudioStream == null) return;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      if (dvbChannel == null) return;

      ITsTimeShift timeshift = _filterTsAnalyzer as ITsTimeShift;
      timeshift.SetPcrPid((short)dvbChannel.PcrPid);
      timeshift.SetPmtPid((short)dvbChannel.PmtPid);
      foreach (PidInfo info in _channelInfo.pids)
      {
        if (info.isAC3Audio || info.isAudio || info.isVideo || info.isDVBSubtitle)
        {
          Log.Log.WriteFile("ss2: set timeshift {0}", info);
          timeshift.AddStream((short)info.pid, (short)info.stream_type, info.language);
        }
      }
    }

    void SetRecorderPids()
    {
      if (_channelInfo == null) return;
      if (_currentChannel == null) return;
      if (_channelInfo.pids.Count == 0) return;
      if (_currentAudioStream == null) return;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      if (dvbChannel == null) return;

      ITsRecorder recorder = _filterTsAnalyzer as ITsRecorder;
      recorder.SetPcrPid((short)dvbChannel.PcrPid);
      bool programStream = true;
      foreach (PidInfo info in _channelInfo.pids)
      {
        if (info.isAC3Audio || info.isAudio || info.isVideo)
        {
          bool addPid = false;
          if (info.isVideo)
          {
            addPid = true;
            if (info.stream_type == 0x10 || info.stream_type == 0x1b)
            {
              programStream = false;
            }
          }
          if (info.isAudio || info.isAC3Audio)
          {
            addPid = true;
          }

          if (addPid)
          {
            Log.Log.WriteFile("ss2: set record {0}", info);
            recorder.AddStream((short)info.pid, (info.isAC3Audio || info.isAudio), info.isVideo);
          }
        }
      }
      if (programStream == false)
      {
        recorder.AddStream((short)0x11, false, false);//sdt
        recorder.AddStream((short)dvbChannel.PmtPid, false, false);
        recorder.SetMode(TimeShiftingMode.TransportStream);
        Log.Log.WriteFile("ss2: record transport stream mode");
      }
      else
      {
        recorder.SetMode(TimeShiftingMode.ProgramStream);
        Log.Log.WriteFile("ss2: record program stream mode");
      }
    }

    /// <summary>
    /// returns true if we timeshift in transport stream mode
    /// false we timeshift in program stream mode
    /// </summary>
    /// <value>true for transport stream, false for program stream.</value>
    public bool IsTimeshiftingTransportStream
    {
      get
      {
        return true;
      }
    }

    /// <summary>
    /// returns true if we record in transport stream mode
    /// false we record in program stream mode
    /// </summary>
    /// <value>true for transport stream, false for program stream.</value>
    public bool IsRecordingTransportStream
    {
      get
      {
        if (_channelInfo == null) return false;
        foreach (PidInfo info in _channelInfo.pids)
        {
          if (info.isVideo)
          {
            if (info.stream_type == 0x10 || info.stream_type == 0x1b) return true;
          }
        }
        return false;
      }
    }

    /// <summary>
    /// sets the filename used for timeshifting
    /// </summary>
    /// <param name="fileName">timeshifting filename</param>
    protected void SetTimeShiftFileName(string fileName)
    {
      if (!CheckThreadId()) return;
      _timeshiftFileName = fileName;
      Log.Log.WriteFile("ss2:SetTimeShiftFileName:{0}", fileName);
      int hr;
      if (_filterTsAnalyzer != null)
      {
        ITsTimeShift timeshift = _filterTsAnalyzer as ITsTimeShift;
        if (timeshift == null)
        {
          Log.Log.WriteFile("ss2:unable to get ITsTimeShift");
          throw new TvException("unable to get ITsTimeShift");
        }
        timeshift.SetTimeShiftingFileName(fileName);
        timeshift.SetMode(TimeShiftingMode.TransportStream);
        if (_channelInfo.pids.Count == 0)
        {
          Log.Log.WriteFile("ss2:SetTimeShiftFileName no pmt received yet");
          _startTimeShifting = true;
        }
        else
        {
          Log.Log.WriteFile("ss2:SetTimeShiftFileName fill in pids");
          _startTimeShifting = false;
          SetTimeShiftPids();
          timeshift.Start();
        }
      }
    }

    /// <summary>
    /// Methods which starts the graph
    /// </summary>
    protected void RunGraph()
    {
      if (!CheckThreadId()) return;
      FilterState state;
      (_graphBuilder as IMediaControl).GetState(10, out state);
      if (state == FilterState.Running)
      {
        Log.Log.WriteFile("ss2:RunGraph already running");

        DVBBaseChannel channel = _currentChannel as DVBBaseChannel;
        SetAnalyzerMapping(channel.PmtPid);
        return;
      }

      Log.Log.WriteFile("ss2:RunGraph");
      _teletextDecoder.ClearBuffer();
      _pmtVersion = -1;

      int hr = 0;
      //hr=_graphBuilder.SetDefaultSyncSource();

      //Log.Log.WriteFile("SetSyncSource:{0:X}", hr);
      /*
      IReferenceClock clock;
      hr = _filterMpeg2DemuxTs.GetSyncSource(out clock);
      Log.Log.WriteFile("GetSyncSource from timeshifting mux:{0:X} {1}", hr, clock);
      
      IMediaFilter mediaFilter = _graphBuilder as IMediaFilter;
      hr = mediaFilter.SetSyncSource(clock);
      Log.Log.WriteFile("SetSyncSource:{0:X}", hr);
      */

      hr = (_graphBuilder as IMediaControl).Run();
      if (hr < 0 || hr > 1)
      {
        Log.Log.WriteFile("ss2:RunGraph returns:0x{0:X}", hr);
        throw new TvException("Unable to start graph");
      }

      _epgGrabbing = false;
      _dateTimeShiftStarted = DateTime.Now;
      DVBBaseChannel dvbChannel = _currentChannel as DVBBaseChannel;
      SetAnalyzerMapping(dvbChannel.PmtPid);
      _pmtTimer.Enabled = true;
      _graphRunning = true;

    }

    /// <summary>
    /// Methods which stops the graph
    /// </summary>
    public void StopGraph()
    {
      Log.Log.WriteFile("ss2:StopGraph");
      if (!CheckThreadId()) return;
      if (_epgGrabbing)
      {
        _epgGrabbing = false;
        if (_epgGrabberCallback != null && _epgGrabbing)
        {
          Log.Log.WriteFile("ss2:cancel epg->stop graph");
          _epgGrabberCallback.OnEpgCancelled();
        }
      }
      _graphRunning = false;
      _epgGrabbing = false;
      _isScanning = false;
      _dateTimeShiftStarted = DateTime.MinValue;
      _dateRecordingStarted = DateTime.MinValue;
      if (_graphBuilder == null) return;
      FilterState state;
      (_graphBuilder as IMediaControl).GetState(10, out state);
      _pmtTimer.Enabled = false;
      _startTimeShifting = false;
      _recordingFileName = "";
      _pmtVersion = -1;
      _channelInfo = new ChannelInfo();
      _currentChannel = null;
      if (_filterTsAnalyzer != null)
      {
        ITsRecorder recorder = _filterTsAnalyzer as ITsRecorder;
        recorder.StopRecord();
        ITsTimeShift timeshift = _filterTsAnalyzer as ITsTimeShift;
        timeshift.Stop();
      }
      if (_teletextDecoder != null)
      {
        _teletextDecoder.ClearBuffer();
      }
      if (state == FilterState.Stopped) return;
      Log.Log.WriteFile("ss2:graph stopped");
      int hr = 0;
      //hr = (_graphBuilder as IMediaControl).StopWhenReady();
      hr = (_graphBuilder as IMediaControl).Stop();
      if (hr < 0 || hr > 1)
      {
        Log.Log.WriteFile("ss2:RunGraph returns:0x{0:X}", hr);
        throw new TvException("Unable to stop graph");
      }
    }


    /// <summary>
    /// Sends the HW pids.
    /// </summary>
    /// <param name="pids">The pids.</param>
    public void SendHWPids(ArrayList pids)
    {
      const int PID_CAPTURE_ALL_INCLUDING_NULLS = 0x2000;//Enables reception of all PIDs in the transport stream including the NULL PID
      // const int PID_CAPTURE_ALL_EXCLUDING_NULLS = 0x2001;//Enables reception of all PIDs in the transport stream excluding the NULL PID.

      if (!DeleteAllPIDs(_interfaceB2C2DataCtrl, 0))
      {
        Log.Log.WriteFile("ss2:DeleteAllPIDs() failed pid:0x2000");
      }
      if (pids.Count == 0 || true)
      {
        Log.Log.WriteFile("ss2:hw pids:all");
        int added = SetPidToPin(_interfaceB2C2DataCtrl, 0, PID_CAPTURE_ALL_INCLUDING_NULLS);
        if (added != 1)
        {
          Log.Log.WriteFile("ss2:SetPidToPin() failed pid:0x2000");
        }
      }
      else
      {
        int maxPids;
        _interfaceB2C2DataCtrl.GetMaxPIDCount(out maxPids);
        for (int i = 0; i < pids.Count && i < maxPids; ++i)
        {
          ushort pid = (ushort)pids[i];
          Log.Log.WriteFile("ss2:hw pids:0x{0:X}", pid);
          SetPidToPin(_interfaceB2C2DataCtrl, 0, pid);
        }
      }
    }


    /// <summary>
    /// adds the mpeg-2 demultiplexer filter to the graph
    /// </summary>
    protected void AddMpeg2DemuxerTif()
    {
      if (!CheckThreadId()) return;
      if (_filterMpeg2DemuxTif != null) return;
      Log.Log.WriteFile("ss2:AddMpeg2DemuxerTif");
      int hr = 0;

      _filterMpeg2DemuxTif = (IBaseFilter)new MPEG2Demultiplexer();
      hr = _graphBuilder.AddFilter(_filterMpeg2DemuxTif, "TIF MPEG2 Demultiplexer");
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:AddMpeg2DemuxerTif returns:0x{0:X}", hr);
        throw new TvException("Unable to add MPEG2 demultiplexer for tif");
      }


      _infTeeMain = (IBaseFilter)new InfTee();
      hr = _graphBuilder.AddFilter(_infTeeMain, "Main Inf Tee");
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:Add main InfTee returns:0x{0:X}", hr);
        throw new TvException("Unable to add  mainInfTee");
      }
    }

    /// <summary>
    /// Connects the mpeg2 demuxers to main tee.
    /// </summary>
    protected void ConnectMpeg2DemuxersToMainTee()
    {
      //multi demux

      //connect the inftee main -> TIF MPEG2 Demultiplexer
      Log.Log.WriteFile("ss2:connect mpeg 2 demuxers to maintee");
      IPin mainTeeOut = DsFindPin.ByDirection(_infTeeMain, PinDirection.Output, 0);
      IPin demuxPinIn = DsFindPin.ByDirection(_filterMpeg2DemuxTif, PinDirection.Input, 0);
      int hr = _graphBuilder.Connect(mainTeeOut, demuxPinIn);
      Release.ComObject("maintee pin0", mainTeeOut);
      Release.ComObject("tifdemux pinin", demuxPinIn);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:Add main InfTee returns:0x{0:X}", hr);
        throw new TvException("Unable to add  mainInfTee");
      }

    }

    /// <summary>
    /// Gets the video audio pins.
    /// </summary>
    protected void GetVideoAudioPins()
    {
      if (!CheckThreadId()) return;
      //multi demux
      Log.Log.WriteFile("ss2:GetVideoAudioPins()");
      if (_filterTsAnalyzer == null)
      {
        _filterTsAnalyzer = (IBaseFilter)new MpTsAnalyzer();
        _graphBuilder.AddFilter(_filterTsAnalyzer, "MediaPortal Ts Analyzer");
        IPin pinTee = DsFindPin.ByDirection(_infTeeMain, PinDirection.Output, 1);
        IPin pin = DsFindPin.ByDirection(_filterTsAnalyzer, PinDirection.Input, 0);
        _graphBuilder.Connect(pinTee, pin);
        Release.ComObject("pinMpTsAnalyzerIn", pin);
        Release.ComObject("pinTee", pinTee);
        _interfaceChannelScan = (ITsChannelScan)_filterTsAnalyzer;
        _interfaceEpgGrabber = (ITsEpgScanner)_filterTsAnalyzer;
        _interfacePmtGrabber = (ITsPmtGrabber)_filterTsAnalyzer;
      }
    }


    /// <summary>
    /// Resets the signal update.
    /// </summary>
    public void ResetSignalUpdate()
    {
      _lastSignalUpdate = DateTime.Now;
    }

    /// <summary>
    /// Updates the signal present.
    /// </summary>
    protected void UpdateSignalPresent()
    {
      if (_graphState == GraphState.Idle || _interfaceB2C2TunerCtrl == null)
      {
        _tunerLocked = false;
        _signalQuality = 0;
        _signalLevel = 0;
        return;
      }
      TimeSpan ts = DateTime.Now - _lastSignalUpdate;
      if (ts.TotalMilliseconds < 500) return;
      _lastSignalUpdate = DateTime.Now;

      int level, quality;
      _tunerLocked = (_interfaceB2C2TunerCtrl.CheckLock() == 0);
      GetSNR(_interfaceB2C2TunerCtrl, out level, out quality);
      if (level < 0) level = 0;
      if (level > 100) level = 100;
      if (quality < 0) quality = 0;
      if (quality > 100) quality = 100;
      _signalQuality = quality;
      _signalLevel = level;
    }
    #region ITeletextCallBack Members

    /// <summary>
    /// callback from the TsWriter filter when it received a new teletext packets
    /// </summary>
    /// <param name="data">teletext data</param>
    /// <param name="packetCount">packetCount</param>
    /// <returns></returns>
    public int OnTeletextReceived(IntPtr data, short packetCount)
    {
      try
      {
        for (int i = 0; i < packetCount; ++i)
        {
          IntPtr packetPtr = new IntPtr(data.ToInt32() + i * 188);
          ProcessPacket(packetPtr);
        }

      }
      catch (Exception ex)
      {
        Log.Log.WriteFile(ex.ToString());
      }
      return 0;
    }


    /// <summary>
    /// processes a single transport packet
    /// Called from BufferCB
    /// </summary>
    /// <param name="ptr">pointer to the transport packet</param>
    public void ProcessPacket(IntPtr ptr)
    {
      if (ptr == IntPtr.Zero) return;

      _packetHeader = _tsHelper.GetHeader((IntPtr)ptr);
      if (_packetHeader.SyncByte != 0x47)
      {
        return;
      }
      if (_packetHeader.TransportError == true)
      {
        return;
      }
      // teletext
      if (_grabTeletext)
      {
        if (_teletextDecoder != null)
        {
          _teletextDecoder.SaveData((IntPtr)ptr);
        }
      }
    }
    #endregion
    #region pidmapping

    /// <summary>
    /// Maps the correct pids to the SI analyzer pin
    /// </summary>
    /// <param name="pmtPid">pid of the PMT</param>
    protected void SetAnalyzerMapping(int pmtPid)
    {
      try
      {
        if (!CheckThreadId()) return;
        Log.Log.WriteFile("ss2:SetAnalyzerMapping {0:X}", pmtPid);
        if (_interfaceChannelScan == null)
        {
          Log.Log.WriteFile("ss2:  no stream analyzer interface");
          return;
        }
        //do grab epg, pat,sdt or nit when not timeshifting
        if (pmtPid > 0)
        {
          _interfacePmtGrabber.SetCallBack(this);
          _interfacePmtGrabber.SetPmtPid(pmtPid);
        }
        Log.Log.WriteFile("ss2:SetAnalyzerMapping done");
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
    }


    /// <summary>
    /// maps the correct pids to the TsFileSink filter and teletext pins
    /// </summary>
    /// <param name="info"></param>
    protected void SetMpegPidMapping(ChannelInfo info)
    {
      //if (!CheckThreadId()) return;
      Log.Log.WriteFile("ss2:SetMpegPidMapping");

      ITsVideoAnalyzer writer = (ITsVideoAnalyzer)_filterTsAnalyzer;
      ArrayList hwPids = new ArrayList();
      hwPids.Add((ushort)0x0);//PAT
      hwPids.Add((ushort)0x1);//CAT
      hwPids.Add((ushort)0x10);//NIT
      hwPids.Add((ushort)0x11);//SDT
      if (_epgGrabbing)
      {
        hwPids.Add((ushort)0xd2);//MHW
        hwPids.Add((ushort)0xd3);//MHW
        hwPids.Add((ushort)0x12);//EIT
      }
      Log.Log.WriteFile("    pcr pid:0x{0:X}", info.pcr_pid);
      Log.Log.WriteFile("    pmt pid:0x{0:X}", info.network_pmt_PID);
      foreach (PidInfo pmtData in info.pids)
      {
        if (pmtData.pid == 0 || pmtData.pid > 0x1fff) continue;
        if (pmtData.isTeletext)
        {
          Log.Log.WriteFile("    map teletext pid:0x{0:X}", pmtData.pid);
          if (GrabTeletext)
          {
            ITsTeletextGrabber grabber = (ITsTeletextGrabber)_filterTsAnalyzer;
            grabber.SetTeletextPid((short)pmtData.pid);
          }
          hwPids.Add((ushort)pmtData.pid);
          _hasTeletext = true;
        }
        if (pmtData.isAC3Audio || pmtData.isAudio)
        {
          if (_currentAudioStream == null || pmtData.isAC3Audio)
          {
            _currentAudioStream = new DVBAudioStream();
            _currentAudioStream.Pid = pmtData.pid;
            _currentAudioStream.Language = pmtData.language;
            _currentAudioStream.StreamType = AudioStreamType.Mpeg2;
            if (pmtData.isAC3Audio)
              _currentAudioStream.StreamType = AudioStreamType.AC3;
          }

          if (_currentAudioStream.Pid == pmtData.pid)
          {
            Log.Log.WriteFile("    map audio pid:0x{0:X}", pmtData.pid);
            writer.SetAudioPid((short)pmtData.pid);
          }
          hwPids.Add((ushort)pmtData.pid);
        }

        if (pmtData.isVideo)
        {
          Log.Log.WriteFile("    map video pid:0x{0:X}", pmtData.pid);
          hwPids.Add((ushort)pmtData.pid);
          writer.SetVideoPid((short)pmtData.pid);
          if (info.pcr_pid > 0 && info.pcr_pid != pmtData.pid)
          {
            hwPids.Add((ushort)info.pcr_pid);
          }
        }
      }
      if (info.network_pmt_PID >= 0 && ((DVBBaseChannel)_currentChannel).ServiceId >= 0)
      {
        hwPids.Add((ushort)info.network_pmt_PID);
        SendHWPids(hwPids);
      }


      if (_startTimeShifting)
      {
        Log.Log.Write("ss2:fill in timeshift pids");
        _startTimeShifting = false;
        ITsTimeShift timeshift = _filterTsAnalyzer as ITsTimeShift;
        timeshift.Reset();
        SetTimeShiftPids();
        timeshift.Start();
      }
      else if (_graphState == GraphState.TimeShifting || _graphState == GraphState.Recording)
      {
        Log.Log.Write("ss2:update timeshift pids");
        SetTimeShiftPids();
      }
    }
    #endregion

    #region recording

    /// <summary>
    /// Starts recording
    /// </summary>
    /// <param name="recordingType">Recording type (content or reference)</param>
    /// <param name="fileName">filename to which to recording should be saved</param>
    /// <param name="startTime">time the recording should start (0=now)</param>
    /// <returns></returns>
    protected void StartRecord(string fileName, RecordingType recordingType, ref long startTime)
    {
      if (!CheckThreadId()) return;
      Log.Log.WriteFile("ss2:StartRecord({0})", fileName);

      int hr;
      if (_filterTsAnalyzer != null)
      {
        Log.Log.WriteFile("ss2:SetRecordingFileName: uses .mpg");
        ITsRecorder record = _filterTsAnalyzer as ITsRecorder;
        hr = record.SetRecordingFileName(fileName);
        if (hr != 0)
        {
          Log.Log.WriteFile("ss2:SetRecordingFileName failed:{0:X}", hr);
        }
        SetRecorderPids();
        hr = record.StartRecord();
        if (hr != 0)
        {
          Log.Log.WriteFile("ss2:StartRecord failed:{0:X}", hr);
        }
      }
      _dateRecordingStarted = DateTime.Now;
    }


    /// <summary>
    /// Stop recording
    /// </summary>
    /// <returns></returns>
    protected void StopRecord()
    {
      if (!CheckThreadId()) return;

      Log.Log.WriteFile("ss2:StopRecord()");

      if (_filterTsAnalyzer != null)
      {
        ITsRecorder record = _filterTsAnalyzer as ITsRecorder;
        record.StopRecord();

      }
    }
    #endregion


    #region epg

    /// <summary>
    /// Start grabbing the epg
    /// </summary>
    public void GrabEpg(BaseEpgGrabber callback)
    {
      if (!CheckThreadId()) return;

      _epgGrabberCallback = callback;
      Log.Log.Write("ss2:grab epg...");
      _interfaceEpgGrabber.SetCallBack((IEpgCallback)callback);
      _interfaceEpgGrabber.GrabEPG();
      _interfaceEpgGrabber.GrabMHW();
      _epgGrabbing = true;
    }
    int getUTC(int val)
    {
      if ((val & 0xF0) >= 0xA0)
        return 0;
      if ((val & 0xF) >= 0xA)
        return 0;
      return ((val & 0xF0) >> 4) * 10 + (val & 0xF);
    }

    /// <summary>
    /// Returns the EPG grabbed or null if epg grabbing is still busy
    /// </summary>
    public List<EpgChannel> Epg
    {
      get
      {
        if (!CheckThreadId()) return null;
        try
        {


          bool dvbReady, mhwReady;
          _interfaceEpgGrabber.IsEPGReady(out dvbReady);
          _interfaceEpgGrabber.IsMHWReady(out mhwReady);
          if (dvbReady == false || mhwReady == false) return null;

          List<EpgChannel> epgChannels = new List<EpgChannel>();
          if (mhwReady)
          {
            short titleCount;
            _interfaceEpgGrabber.GetMHWTitleCount(out titleCount);
            Log.Log.WriteFile("mhw ready {0}", titleCount);
            for (int i = 0; i < titleCount; ++i)
            {
              short id = 0, transportid = 0, networkid = 0, channelnr = 0, channelid = 0, programid = 0, themeid = 0, PPV = 0, duration = 0;
              byte summaries = 0;
              uint datestart = 0, timestart = 0;
              IntPtr ptrTitle, ptrProgramName;
              IntPtr ptrChannelName, ptrSummary, ptrTheme;
              _interfaceEpgGrabber.GetMHWTitle((short)i, ref id, ref transportid, ref networkid, ref channelnr, ref programid, ref themeid, ref PPV, ref summaries, ref duration, ref datestart, ref timestart, out ptrTitle, out ptrProgramName);
              _interfaceEpgGrabber.GetMHWChannel(channelnr, ref channelid, ref networkid, ref transportid, out ptrChannelName);
              _interfaceEpgGrabber.GetMHWSummary(programid, out ptrSummary);
              _interfaceEpgGrabber.GetMHWTheme(themeid, out ptrTheme);

              string channelName, title, programName, summary, theme;
              channelName = Marshal.PtrToStringAnsi(ptrChannelName);
              title = Marshal.PtrToStringAnsi(ptrTitle);
              programName = Marshal.PtrToStringAnsi(ptrProgramName);
              summary = Marshal.PtrToStringAnsi(ptrSummary);
              theme = Marshal.PtrToStringAnsi(ptrTheme);
              EpgChannel epgChannel = null;

              if (channelName == null) channelName = "";
              if (title == null) title = "";
              if (programName == null) programName = "";
              if (summary == null) summary = "";
              if (theme == null) theme = "";
              channelName = channelName.Trim();
              title = title.Trim();
              programName = programName.Trim();
              summary = summary.Trim();
              theme = theme.Trim();

              foreach (EpgChannel chan in epgChannels)
              {
                DVBBaseChannel dvbChan = (DVBBaseChannel)chan.Channel;
                if (dvbChan.NetworkId == networkid && dvbChan.TransportId == transportid && dvbChan.ServiceId == channelnr)
                {
                  epgChannel = chan;
                  break;
                }
              }
              if (epgChannel == null)
              {
                DVBBaseChannel dvbChan = new DVBBaseChannel();
                dvbChan.NetworkId = networkid;
                dvbChan.TransportId = transportid;
                dvbChan.ServiceId = channelnr;
                dvbChan.Name = channelName;
                epgChannel = new EpgChannel();
                epgChannel.Channel = dvbChan;
                epgChannels.Add(epgChannel);
              }

              uint d1 = datestart;
              uint m = timestart & 0xff;
              uint h1 = (timestart >> 16) & 0xff;
              DateTime programStartTime = System.DateTime.Now;
              DateTime dayStart = System.DateTime.Now;
              dayStart = dayStart.Subtract(new TimeSpan(1, dayStart.Hour, dayStart.Minute, dayStart.Second, dayStart.Millisecond));
              int day = (int)dayStart.DayOfWeek;

              programStartTime = dayStart;
              int minVal = (int)((d1 - day) * 86400 + h1 * 3600 + m * 60);
              if (minVal < 21600)
                minVal += 604800;

              programStartTime = programStartTime.AddSeconds(minVal);

              EpgProgram program = new EpgProgram(programStartTime, programStartTime.AddMinutes(duration));

              EpgLanguageText epgLang = new EpgLanguageText("ALL", title, summary, theme);
              program.Text.Add(epgLang);
              epgChannel.Programs.Add(program);
            }
          }
          if (dvbReady)
          {
            Log.Log.WriteFile("dvb ready");
            uint channelCount = 0;
            ushort networkid = 0;
            ushort transportid = 0;
            ushort serviceid = 0;
            _interfaceEpgGrabber.GetEPGChannelCount(out channelCount);
            for (uint x = 0; x < channelCount; ++x)
            {
              _interfaceEpgGrabber.GetEPGChannel((uint)x, ref networkid, ref transportid, ref serviceid);
              EpgChannel epgChannel = new EpgChannel();
              DVBBaseChannel chan = new DVBBaseChannel();
              chan.NetworkId = networkid;
              chan.TransportId = transportid;
              chan.ServiceId = serviceid;
              epgChannel.Channel = chan;


              uint eventCount = 0;
              _interfaceEpgGrabber.GetEPGEventCount((uint)x, out eventCount);
              for (uint i = 0; i < eventCount; ++i)
              {
                uint start_time_MJD = 0, start_time_UTC = 0, duration = 0, languageId = 0, languageCount = 0;
                string title, description, genre;
                IntPtr ptrTitle = IntPtr.Zero;
                IntPtr ptrDesc = IntPtr.Zero;
                IntPtr ptrGenre = IntPtr.Zero;
                _interfaceEpgGrabber.GetEPGEvent((uint)x, (uint)i, out languageCount, out start_time_MJD, out start_time_UTC, out duration, out ptrGenre);
                genre = Marshal.PtrToStringAnsi(ptrGenre);

                int duration_hh = getUTC((int)((duration >> 16)) & 255);
                int duration_mm = getUTC((int)((duration >> 8)) & 255);
                int duration_ss = 0;//getUTC((int) (duration )& 255);
                int starttime_hh = getUTC((int)((start_time_UTC >> 16)) & 255);
                int starttime_mm = getUTC((int)((start_time_UTC >> 8)) & 255);
                int starttime_ss = 0;//getUTC((int) (start_time_UTC )& 255);

                if (starttime_hh > 23) starttime_hh = 23;
                if (starttime_mm > 59) starttime_mm = 59;
                if (starttime_ss > 59) starttime_ss = 59;

                if (duration_hh > 23) duration_hh = 23;
                if (duration_mm > 59) duration_mm = 59;
                if (duration_ss > 59) duration_ss = 59;

                // convert the julian date
                int year = (int)((start_time_MJD - 15078.2) / 365.25);
                int month = (int)((start_time_MJD - 14956.1 - (int)(year * 365.25)) / 30.6001);
                int day = (int)(start_time_MJD - 14956 - (int)(year * 365.25) - (int)(month * 30.6001));
                int k = (month == 14 || month == 15) ? 1 : 0;
                year += 1900 + k; // start from year 1900, so add that here
                month = month - 1 - k * 12;
                int starttime_y = year;
                int starttime_m = month;
                int starttime_d = day;

                try
                {
                  DateTime dtUTC = new DateTime(starttime_y, starttime_m, starttime_d, starttime_hh, starttime_mm, starttime_ss, 0);
                  DateTime dtStart = dtUTC.ToLocalTime();
                  DateTime dtEnd = dtStart.AddHours(duration_hh);
                  dtEnd = dtEnd.AddMinutes(duration_mm);
                  dtEnd = dtEnd.AddSeconds(duration_ss);
                  EpgProgram epgProgram = new EpgProgram(dtStart, dtEnd);
                  //EPGEvent newEvent = new EPGEvent(genre, dtStart, dtEnd);
                  for (int z = 0; z < languageCount; ++z)
                  {
                    _interfaceEpgGrabber.GetEPGLanguage((uint)x, (uint)i, (uint)z, out languageId, out ptrTitle, out ptrDesc);
                    title = Marshal.PtrToStringAnsi(ptrTitle);
                    description = Marshal.PtrToStringAnsi(ptrDesc);
                    string language = String.Empty;
                    language += (char)((languageId >> 16) & 0xff);
                    language += (char)((languageId >> 8) & 0xff);
                    language += (char)((languageId) & 0xff);

                    if (title == null) title = "";
                    if (description == null) description = "";
                    if (language == null) language = "";
                    if (genre == null) genre = "";
                    title = title.Trim();
                    description = description.Trim();
                    language = language.Trim();
                    genre = genre.Trim();

                    EpgLanguageText epgLangague = new EpgLanguageText(language, title, description, genre);
                    epgProgram.Text.Add(epgLangague);


                  }
                  epgChannel.Programs.Add(epgProgram);
                }
                catch (Exception ex)
                {
                  Log.Log.Write(ex);
                }
              }//for (uint i = 0; i < eventCount; ++i)
              epgChannel.Sort();
              epgChannels.Add(epgChannel);
            }//for (uint x = 0; x < channelCount; ++x)
          }
          return epgChannels;
        }
        catch (Exception ex)
        {
          Log.Log.Write(ex);
          return new List<EpgChannel>();
        }
      }
    }

    #endregion

    #region properties
    /// <summary>
    /// Gets/sets the card name
    /// </summary>
    public string Name
    {
      get
      {
        return _tunerDevice.Name;
      }
      set
      {
      }
    }

    /// <summary>
    /// gets the current filename used for recording
    /// </summary>
    public string FileName
    {
      get
      {
        return _recordingFileName;
      }
    }

    /// <summary>
    /// returns true if card is currently recording
    /// </summary>
    public bool IsRecording
    {
      get
      {
        return (_graphState == GraphState.Recording);
      }
    }

    /// <summary>
    /// returns true if card is currently timeshifting
    /// </summary>
    public bool IsTimeShifting
    {
      get
      {
        return (_graphState == GraphState.TimeShifting);
      }
    }

    /// <summary>
    /// returns the IChannel to which the card is currently tuned
    /// </summary>
    public IChannel Channel
    {
      get
      {
        return _currentChannel;
      }
    }
    #endregion

    #region epg & scanning
    /// <summary>
    /// returns the ITVScanning interface used for scanning channels
    /// </summary>
    public ITVScanning ScanningInterface
    {
      get
      {
        if (!CheckThreadId()) return null;
        return new DVBSS2canning(this);
      }
    }

    #endregion

    #region teletext
    /// <summary>
    /// Turn on/off teletext grabbing
    /// </summary>
    public bool GrabTeletext
    {
      get
      {
        return _grabTeletext;
      }
      set
      {
        _grabTeletext = value;
        ITsTeletextGrabber grabber = (ITsTeletextGrabber)_filterTsAnalyzer;
        if (_grabTeletext)
        {
          int teletextPid = -1;
          foreach (PidInfo pidInfo in _channelInfo.pids)
          {
            if (pidInfo.isTeletext)
            {
              teletextPid = pidInfo.pid;
              break;
            }
          }

          if (teletextPid == -1)
          {
            grabber.Stop();
            _grabTeletext = false;
            return;
          }
          grabber.SetCallBack(this);
          grabber.SetTeletextPid((short)teletextPid);
          grabber.Start();
        }
        else
        {
          grabber.Stop();
        }
      }
    }

    /// <summary>
    /// Property which returns true when the current channel contains teletext
    /// </summary>
    public bool HasTeletext
    {
      get
      {
        return (_hasTeletext);
      }
    }
    /// <summary>
    /// returns the ITeletext interface used for retrieving the teletext pages
    /// </summary>
    public ITeletext TeletextDecoder
    {
      get
      {
        return null;
      }
    }
    #endregion

    #region tuning & recording
    /// <summary>
    /// tune the card to the channel specified by IChannel
    /// </summary>
    /// <param name="channel">channel to tune</param>
    /// <returns></returns>
    public bool TuneScan(IChannel channel)
    {
      Log.Log.WriteFile("ss2:TuneScan({0})", channel);
      bool result = Tune(channel);
      RunGraph();
      return result;
    }
    /// <summary>
    /// Tunes the specified channel.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>true if succeeded else false</returns>
    public bool Tune(IChannel channel)
    {

      Log.Log.WriteFile("ss2:Tune({0})", channel);
      if (_epgGrabbing)
      {
        _epgGrabbing = false;
        if (_epgGrabberCallback != null && _epgGrabbing)
        {
          _epgGrabberCallback.OnEpgCancelled();
        }
      }
      DVBSChannel dvbsChannel = channel as DVBSChannel;
      _hasTeletext = false;
      if (dvbsChannel == null)
      {
        Log.Log.WriteFile("Channel is not a DVBS channel!!! {0}", channel.GetType().ToString());
        return false;
      }

      DVBSChannel oldChannel = _currentChannel as DVBSChannel;
      if (_currentChannel != null)
      {
        if (oldChannel.Equals(channel))
        {
          Log.Log.WriteFile("ss2:already tuned on this channel");
          return true;
        }
      }
      _pmtVersion = -1;
      _startTimeShifting = false;
      _channelInfo = new ChannelInfo();
      _currentChannel = channel;
      if (_graphState == GraphState.Idle)
      {
        BuildGraph();
      }
      if (_interfaceEpgGrabber != null)
      {
        _interfaceEpgGrabber.Reset();
      }
      _startTimeShifting = false;
      int lnbFrequency = 10600000;
      bool hiBand = true;
      if (dvbsChannel.Frequency >= 11700000)
      {
        lnbFrequency = 10600000;
        hiBand = true;
      }
      else
      {
        lnbFrequency = 9750000;
        hiBand = false;
      }

      int frequency = (int)dvbsChannel.Frequency;
      if (frequency > 13000)
        frequency /= 1000;
      Log.Log.WriteFile("ss2:  Transponder Frequency:{0} MHz", frequency);
      int hr = _interfaceB2C2TunerCtrl.SetFrequency(frequency);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetFrequencyKHz() failed:0x{0:X}", hr);
        return false;
      }
      int lnbKhzTone;
      //GetDisEqcSettings(ref ch, out lowOsc, out hiOsc, out lnbKhzTone, out disEqcUsed);

      if (lnbFrequency >= dvbsChannel.Frequency)
      {
        Log.Log.WriteFile("ss2:  Error: LNB Frequency must be less than Transponder frequency");
      }
      Log.Log.WriteFile("ss2:  SymbolRate:{0} KS/s", dvbsChannel.SymbolRate);
      hr = _interfaceB2C2TunerCtrl.SetSymbolRate(dvbsChannel.SymbolRate);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetSymbolRate() failed:0x{0:X}", hr);
        return false;
      }

      int fec = (int)FecType.Fec_Auto;
      Log.Log.WriteFile("ss2:  Fec:{0} {1}", ((FecType)fec), fec);
      hr = _interfaceB2C2TunerCtrl.SetFec(fec);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetFec() failed:0x{0:X}", hr);
        return false;
      }

      //0=horizontal,1=vertical
      int polarity = 0;
      if (dvbsChannel.Polarisation == Polarisation.LinearV) polarity = 1;
      Log.Log.WriteFile("ss2:  Polarity:{0} {1}", dvbsChannel.Polarisation, polarity);
      hr = _interfaceB2C2TunerCtrl.SetPolarity(polarity);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetPolarity() failed:0x{0:X}", hr);
        return false;
      }

      lnbKhzTone = 22;
      LNBSelectionType lnbSelection = LNBSelectionType.Lnb0;
      switch (lnbKhzTone)
      {
        case 0:
          lnbSelection = LNBSelectionType.Lnb0;
          break;
        case 22:
          lnbSelection = LNBSelectionType.Lnb22kHz;
          break;
        case 33:
          lnbSelection = LNBSelectionType.Lnb33kHz;
          break;
        case 44:
          lnbSelection = LNBSelectionType.Lnb44kHz;
          break;
      }
      if (hiBand == false)
      {
        lnbSelection = LNBSelectionType.Lnb0;
      }

      Log.Log.WriteFile("ss2:  Lnb:{0}", lnbSelection);
      hr = _interfaceB2C2TunerCtrl.SetLnbKHz((int)lnbSelection);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetLnbKHz() failed:0x{0:X}", hr);
        return false;
      }

      SS2DisEqcType disType = SS2DisEqcType.None;
      switch (dvbsChannel.DisEqc)
      {
        case DisEqcType.None: // none
          disType = SS2DisEqcType.None;
          break;
        case DisEqcType.SimpleA: // Simple A
          disType = SS2DisEqcType.Simple_A;
          break;
        case DisEqcType.SimpleB: // Simple B
          disType = SS2DisEqcType.Simple_B;
          break;
        case DisEqcType.Level1AA: // Level 1 A/A
          disType = SS2DisEqcType.Level_1_A_A;
          break;
        case DisEqcType.Level1BA: // Level 1 B/A
          disType = SS2DisEqcType.Level_1_B_A;
          break;
        case DisEqcType.Level1AB: // Level 1 A/B
          disType = SS2DisEqcType.Level_1_A_B;
          break;
        case DisEqcType.Level1BB: // Level 1 B/B
          disType = SS2DisEqcType.Level_1_B_B;
          break;
      }
      Log.Log.WriteFile("ss2:  Diseqc:{0} {1}", disType, disType);
      hr = _interfaceB2C2TunerCtrl.SetDiseqc((int)disType);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetDiseqc() failed:0x{0:X}", hr);
        return false;
      }

      int switchFreq = lnbFrequency / 1000;//in MHz
      Log.Log.WriteFile("ss2:  LNBFrequency:{0} MHz", switchFreq);
      hr = _interfaceB2C2TunerCtrl.SetLnbFrequency(switchFreq);
      if (hr != 0)
      {
        Log.Log.WriteFile("ss2:SetLnbFrequency() failed:0x{0:X}", hr);
        return false;
      }

#if FORM
      _newPMT = false;
#endif
      _currentAudioStream = null;
      hr = _interfaceB2C2TunerCtrl.SetTunerStatus();
      _interfaceB2C2TunerCtrl.CheckLock();
      if (((uint)hr) == (uint)0x90010115)
      {
        Log.Log.WriteFile("ss2:could not lock tuner");
      }
      if (hr != 0)
      {
        hr = _interfaceB2C2TunerCtrl.SetTunerStatus();
        if (hr != 0)
          hr = _interfaceB2C2TunerCtrl.SetTunerStatus();
        if (hr != 0)
        {
          Log.Log.WriteFile("ss2:SetTunerStatus failed:0x{0:X}", hr);
          return false;
        }
      }
      _interfaceB2C2TunerCtrl.CheckLock();
      _lastSignalUpdate = DateTime.MinValue;
      UpdateSignalPresent();
      ArrayList pids = new ArrayList();
      pids.Add((ushort)0x0);//pat
      pids.Add((ushort)0x11);//sdt
      pids.Add((ushort)0x1fff);//padding stream
      if (_currentChannel != null)
      {
        DVBBaseChannel ch = (DVBBaseChannel)_currentChannel;
        if (ch.PmtPid > 0)
        {
          pids.Add((ushort)ch.PmtPid);//sdt
        }
      }
      SendHWPids(pids);


      SetAnalyzerMapping(dvbsChannel.PmtPid);
      Log.Log.WriteFile("ss2:tune done");
      return true;
    }


    /// <summary>
    /// Starts timeshifting. Note card has to be tuned first
    /// </summary>
    /// <param name="fileName">filename used for the timeshiftbuffer</param>
    /// <returns></returns>
    public bool StartTimeShifting(string fileName)
    {
      Log.Log.WriteFile("ss2:StartTimeShifting()");
      if (!CheckThreadId()) return false;
      if (_graphState == GraphState.TimeShifting)
      {
        return true;
      }
      if (_graphState == GraphState.Idle)
      {
        BuildGraph();
      }

      if (_currentChannel == null)
      {
        Log.Log.WriteFile("ss2:StartTimeShifting not tuned to a channel");
        throw new TvException("StartTimeShifting not tuned to a channel");
      }
      DVBBaseChannel channel = (DVBBaseChannel)_currentChannel;
      if (channel.NetworkId == -1 || channel.TransportId == -1 || channel.ServiceId == -1)
      {
        Log.Log.WriteFile("ss2:StartTimeShifting not tuned to a channel but to a transponder");
        throw new TvException("StartTimeShifting not tuned to a channel but to a transponder");
      }
      if (_graphState == GraphState.Created)
      {
        SetTimeShiftFileName(fileName);
      }

      RunGraph();
      _graphState = GraphState.TimeShifting;
      Tune(Channel);
      return true;
    }

    /// <summary>
    /// Stops timeshifting
    /// </summary>
    /// <returns></returns>
    public bool StopTimeShifting()
    {
      Log.Log.WriteFile("ss2:StopTimeShifting()");
      if (!CheckThreadId()) return false;
      if (_graphState != GraphState.TimeShifting)
      {
        return true;
      }
      StopGraph();
      _graphState = GraphState.Created;
      return true;
    }

    /// <summary>
    /// Starts recording
    /// </summary>
    /// <param name="recordingType">Recording type (content or reference)</param>
    /// <param name="fileName">filename to which to recording should be saved</param>
    /// <param name="startTime">time the recording should start (0=now)</param>
    /// <returns></returns>
    public bool StartRecording(RecordingType recordingType, string fileName, long startTime)
    {
      Log.Log.WriteFile("ss2:StartRecording to {0}", fileName);
      if (!CheckThreadId()) return false;
      if (_graphState == GraphState.Recording) return false;
      if (_graphState != GraphState.TimeShifting)
      {
        throw new TvException("Card must be timeshifting before starting recording");
      }
      _recordingFileName = fileName;
      StartRecord(fileName, recordingType, ref startTime);
      Log.Log.WriteFile("ss2:Started recording on {0}", startTime);
      _graphState = GraphState.Recording;
      return true;
    }
    /// <summary>
    /// Stop recording
    /// </summary>
    /// <returns></returns>
    public bool StopRecording()
    {
      Log.Log.WriteFile("ss2:StopRecording");
      if (!CheckThreadId()) return false;
      if (_graphState != GraphState.Recording) return false;
      StopRecord();
      _recordingFileName = "";
      _graphState = GraphState.TimeShifting;
      return true;
    }
    #endregion

    #region audio streams
    #region audio streams
    /// <summary>
    /// returns the list of available audio streams
    /// </summary>
    public List<IAudioStream> AvailableAudioStreams
    {
      get
      {
        if (!CheckThreadId()) return null;
        List<IAudioStream> streams = new List<IAudioStream>();
        foreach (PidInfo info in _channelInfo.pids)
        {
          if (info.isAC3Audio)
          {
            DVBAudioStream stream = new DVBAudioStream();
            stream.Language = info.language;
            stream.Pid = info.pid;
            stream.StreamType = AudioStreamType.AC3;
            streams.Add(stream);
          }
          else if (info.isAudio)
          {
            DVBAudioStream stream = new DVBAudioStream();
            stream.Language = info.language;
            stream.Pid = info.pid;
            stream.StreamType = AudioStreamType.Mpeg2;
            streams.Add(stream);
          }
        }
        return streams;
      }
    }

    /// <summary>
    /// get/set the current selected audio stream
    /// </summary>
    public IAudioStream CurrentAudioStream
    {
      get
      {
        return _currentAudioStream;
      }
      set
      {
        if (!CheckThreadId()) return;
        List<IAudioStream> streams = AvailableAudioStreams;
        DVBAudioStream audioStream = (DVBAudioStream)value;
        if (_filterTsAnalyzer != null)
        {
          ITsVideoAnalyzer writer = (ITsVideoAnalyzer)_filterTsAnalyzer;
          writer.SetAudioPid((short)audioStream.Pid);
/*
          ITsTimeShift timeshift = _filterTsAnalyzer as ITsTimeShift;
          if (_currentAudioStream != null)
          {
            timeshift.RemoveStream((short)_currentAudioStream.Pid);
          }
          if (audioStream.StreamType == AudioStreamType.AC3)
            timeshift.AddStream((short)audioStream.Pid, 0x81, audioStream.Language);
          else
            timeshift.AddStream((short)audioStream.Pid, 3, audioStream.Language);
*/
        }
        _currentAudioStream = audioStream;
        _pmtVersion = -1;
        SendPmtToCam();
      }
    }
    #endregion

    #endregion

    #region quality control
    /// <summary>
    /// Get/Set the quality
    /// </summary>
    public IQuality Quality
    {
      get
      {
        return null;
      }
      set
      {
        ;
      }
    }

    /// <summary>
    /// Property which returns true if card supports quality control
    /// </summary>
    public bool SupportsQualityControl
    {
      get
      {
        return false;
      }
    }
    #endregion

    #region properties
    /// <summary>
    /// When the tuner is locked onto a signal this property will return true
    /// otherwise false
    /// </summary>
    public bool IsTunerLocked
    {
      get
      {
        UpdateSignalPresent();
        return _tunerLocked;
      }
    }

    /// <summary>
    /// returns the signal quality
    /// </summary>
    public int SignalQuality
    {
      get
      {
        UpdateSignalPresent();
        if (_signalLevel < 0) _signalQuality = 0;
        if (_signalLevel > 100) _signalQuality = 100;
        return _signalQuality;
      }
    }

    /// <summary>
    /// returns the ITsChannelScan interface for the graph
    /// </summary>
    public ITsChannelScan StreamAnalyzer
    {
      get
      {
        return _interfaceChannelScan;
      }
    }
    /// <summary>
    /// returns the signal level
    /// </summary>
    public int SignalLevel
    {
      get
      {
        UpdateSignalPresent();
        if (_signalLevel < 0) _signalLevel = 0;
        if (_signalLevel > 100) _signalLevel = 100;
        return _signalLevel;
      }
    }
    #endregion

    #region idisposable
    /// <summary>
    /// Disposes this instance.
    /// </summary>
    public void Dispose()
    {
      if (_graphBuilder == null) return;
      if (!CheckThreadId()) return;


      if (_epgGrabbing)
      {
        _epgGrabbing = false;
        if (_epgGrabberCallback != null && _epgGrabbing)
        {
          _epgGrabberCallback.OnEpgCancelled();
        }
      }
      Log.Log.WriteFile("ss2:Decompose");
      int hr = 0;
      _graphRunning = false;

      // Decompose the graph
      //hr = (_graphBuilder as IMediaControl).StopWhenReady();
      hr = (_graphBuilder as IMediaControl).Stop();

      FilterGraphTools.RemoveAllFilters(_graphBuilder);

      _interfaceChannelScan = null;
      _interfaceEpgGrabber = null;
      _interfaceB2C2DataCtrl = null;
      _interfaceB2C2TunerCtrl = null; ;

      if (_filterMpeg2DemuxTif != null)
      {
        Release.ComObject("MPEG2 demux filter", _filterMpeg2DemuxTif); _filterMpeg2DemuxTif = null;
      }
      if (_filterB2C2Adapter != null)
      {
        Release.ComObject("tuner filter", _filterB2C2Adapter); _filterB2C2Adapter = null;
      }
      if (_filterTIF != null)
      {
        Release.ComObject("TIF filter", _filterTIF); _filterTIF = null;
      }
      if (_filterSectionsAndTables != null)
      {
        Release.ComObject("secions&tables filter", _filterSectionsAndTables); _filterSectionsAndTables = null;
      }


      if (_filterTsAnalyzer != null)
      {
        Release.ComObject("_filterMpTsAnalyzer", _filterTsAnalyzer); _filterTsAnalyzer = null;
      }
      if (_infTeeMain != null)
      {
        Release.ComObject("_filterMpTsAnalyzer", _infTeeMain); _infTeeMain = null;
      }

      _rotEntry.Dispose();
      if (_capBuilder != null)
      {
        Release.ComObject("capture builder", _capBuilder); _capBuilder = null;
      }
      if (_graphBuilder != null)
      {
        Release.ComObject("graph builder", _graphBuilder); _graphBuilder = null;
      }

      if (_tunerDevice != null)
      {
        DevicesInUse.Instance.Remove(_tunerDevice);
        _tunerDevice = null;
      }

      if (_teletextDecoder != null)
      {
        _teletextDecoder.ClearBuffer();
        _teletextDecoder = null;
      }

    }

    #endregion

    /// <summary>
    /// timer callback. This method checks if a new PMT has been received
    /// and ifso updates the various pid mappings and updates the CI interface
    /// with the new PMT
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    void _pmtTimer_ElapsedForm(object sender, EventArgs args)
    {
      _pmtTimer_Elapsed(null, null);
    }
    void _pmtTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
      try
      {
        if (_graphRunning == false) return;
#if FORM
        if (_newPMT)
        {
          if (SendPmtToCam())
          {
                SetMpegPidMapping(_channelInfo);
          } 
          _newPMT=false;
        }
#endif

      }
      catch (Exception ex)
      {
        Log.Log.WriteFile("ss2:{0}", ex.Message);
        Log.Log.WriteFile("ss2:{0}", ex.Source);
        Log.Log.WriteFile("ss2:{0}", ex.StackTrace);
      }
    }
    #region pidmapping


    #endregion
    #endregion
    /// <summary>
    /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override string ToString()
    {
      return _tunerDevice.Name;
    }

    /// <summary>
    /// Gets/sets the card device
    /// </summary>
    public string DevicePath
    {
      get
      {
        return _tunerDevice.DevicePath;
      }
    }
    /// <summary>
    /// gets the current filename used for timeshifting
    /// </summary>
    public string TimeShiftFileName
    {
      get
      {
        return _timeshiftFileName;
      }
    }

    /// <summary>
    /// Method to check if card can tune to the channel specified
    /// </summary>
    /// <returns>true if card can tune to the channel otherwise false</returns>
    public bool CanTune(IChannel channel)
    {
      if ((channel as DVBSChannel) == null) return false;
      return true;
    }

    /// <summary>
    /// returns true if card is currently grabbing the epg
    /// </summary>
    public bool IsEpgGrabbing
    {
      get
      {
        return _epgGrabbing;
      }
      set
      {
        if (_epgGrabbing && value == false) _interfaceEpgGrabber.Reset();
        _epgGrabbing = value;
      }
    }

    /// <summary>
    /// returns true if card is currently scanning
    /// </summary>
    public bool IsScanning
    {
      get
      {
        return _isScanning;
      }
      set
      {
        _isScanning = value;
        if (_isScanning)
        {
          _epgGrabbing = false;
          if (_epgGrabberCallback != null && _epgGrabbing)
          {
            _epgGrabberCallback.OnEpgCancelled();
          }
        }
      }
    }

    /// <summary>
    /// returns the min. channel numbers for analog cards
    /// </summary>
    public int MinChannel
    {
      get { return -1; }
    }
    /// <summary>
    /// returns the max. channel number for analog cards
    /// </summary>
    /// <value>The max channel.</value>
    public int MaxChannel
    {
      get { return -1; }
    }

    /// <summary>
    /// returns the date/time when timeshifting has been started for the card specified
    /// </summary>
    /// <returns>DateTime containg the date/time when timeshifting was started</returns>
    public DateTime StartOfTimeShift
    {
      get
      {
        return _dateTimeShiftStarted;
      }
    }
    /// <summary>
    /// returns the date/time when recording has been started for the card specified
    /// </summary>
    /// <returns>DateTime containg the date/time when recording was started</returns>
    public DateTime RecordingStarted
    {
      get
      {
        return _dateRecordingStarted;
      }
    }


    /// <summary>
    /// Returns true when unscrambled audio/video is received otherwise false
    /// </summary>
    /// <returns>true of false</returns>
    public bool IsReceivingAudioVideo
    {
      get
      {
        if (_graphRunning == false) return false;
        if (_filterTsAnalyzer == null) return false;
        if (_currentChannel == null) return false;
        ITsVideoAnalyzer writer = (ITsVideoAnalyzer)_filterTsAnalyzer;
        short audioEncrypted = 0;
        short videoEncrypted = 0;
        writer.IsAudioEncrypted(out audioEncrypted);
        if (_currentChannel.IsTv)
        {
          writer.IsVideoEncrypted(out videoEncrypted);
        }
        return ((audioEncrypted == 0) && (videoEncrypted == 0));
      }
    }
    #region pmtcallback interface
    /// <summary>
    /// Called when  a new PMT is received
    /// </summary>
    /// <returns></returns>
    public int OnPMTReceived()
    {
      try
      {
        Log.Log.WriteFile("ss2:OnPMTReceived()");
        if (_graphRunning == false) return 0;
#if FORM
        _newPMT = true;
#else
        if (SendPmtToCam())
        {
          SetMpegPidMapping(_channelInfo);
        }
#endif
      }
      catch (Exception ex)
      {
        Log.Log.Write(ex);
      }
      return 0;
    }
    #endregion

    /// <summary>
    /// Sends the PMT to cam.
    /// </summary>
    protected bool SendPmtToCam()
    {
      lock (this)
      {
        DVBBaseChannel channel = _currentChannel as DVBBaseChannel;
        if (channel == null) return false;
        IntPtr pmtMem = Marshal.AllocCoTaskMem(4096);// max. size for pmt
        try
        {
          int pmtLength = _interfacePmtGrabber.GetPMTData(pmtMem);
          if (pmtLength != -1)
          {
            byte[] pmt = new byte[pmtLength];
            int version = -1;
            Marshal.Copy(pmtMem, pmt, 0, pmtLength);
            version = ((pmt[5] >> 1) & 0x1F);
            int pmtProgramNumber = (pmt[3] << 8) + pmt[4];
            if (pmtProgramNumber == channel.ServiceId)
            {
              if (_pmtVersion != version)
              {
                Log.Log.WriteFile("ss2: SendPmtToCam() version={0}", version);
                _channelInfo = new ChannelInfo();
                _channelInfo.DecodePmt(pmt);
                _channelInfo.network_pmt_PID = channel.PmtPid;
                _channelInfo.pcr_pid = channel.PcrPid;


                _pmtVersion = version;
                return true;
              }
            }
          }
        }
        catch (Exception ex)
        {
          Log.Log.Write(ex);
        }
        finally
        {
          Marshal.FreeCoTaskMem(pmtMem);
        }
      }
      return false;
    }

    public CamType CamType
    {
      get
      {
        return CamType.Default;
      }
      set
      {
      }
    }
  }
}

