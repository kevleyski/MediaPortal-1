#region Copyright (C) 2005-2010 Team MediaPortal

// Copyright (C) 2005-2010 Team MediaPortal
// http://www.team-mediaportal.com
// 
// MediaPortal is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MediaPortal is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MediaPortal. If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections;
using System.IO;
using System.Windows.Forms;
using MediaPortal.Configuration;
using MediaPortal.ExtensionMethods;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using MediaPortal.Playlists;
using MediaPortal.Profile;
using MediaPortal.Util;
using Win32.Utils.Cd;

namespace MediaPortal.Ripper
{
  /// <summary>
  /// AutoPlay functionality.
  /// </summary>
  public class AutoPlay
  {
    #region base variables

    private static DeviceVolumeMonitor _deviceMonitor;
    private static string m_dvd;
    private static string m_audiocd;
    private static bool m_nowPlayingScreen;
    private static ArrayList allfiles;

    // A hidden window to allow us to listen to WndProc messages
    // Winamp viz doesn't like when it receives notify that the WndProc handler has changed
    private static NativeWindow _nativeWindow;
    private static IntPtr _windowHandle;

    private enum MediaType
    {
      UNKNOWN = 0,
      DVD = 1,
      AUDIO_CD = 2,
      PHOTOS = 3,
      VIDEOS = 4,
      AUDIO = 5,
      BLURAY = 6,
      HDDVD = 7,
      VCD = 8
    }

    #endregion

    /// <summary>
    /// singleton. Dont allow any instance of this class so make the constructor private
    /// </summary>
    private AutoPlay() {}

    /// <summary>
    /// Static constructor of the autoplay class.
    /// </summary>
    static AutoPlay()
    {
      m_dvd = "No";
      m_audiocd = "No";
      m_nowPlayingScreen = true;
      allfiles = new ArrayList();

      _nativeWindow = new NativeWindow();
      CreateParams cp = new CreateParams();
      _nativeWindow.CreateHandle(cp);
      _windowHandle = _nativeWindow.Handle;
    }

    ~AutoPlay()
    {
      _deviceMonitor.SafeDispose();
      _deviceMonitor = null;
    }

    /// <summary>
    /// Starts listening for events on the optical drives.
    /// </summary>
    public static void StartListening()
    {
      LoadSettings();
      StartListeningForEvents();
    }

    /// <summary>
    /// Stops listening for events on the optical drives and cleans up.
    /// </summary>
    public static void StopListening()
    {
      StopListeningForEvents();
    }

    #region initialization + serialization

    private static void LoadSettings()
    {
      using (Settings xmlreader = new MPSettings())
      {
        m_dvd = xmlreader.GetValueAsString("dvdplayer", "autoplay", "Ask");
        m_audiocd = xmlreader.GetValueAsString("audioplayer", "autoplay", "No");
        m_nowPlayingScreen = xmlreader.GetValueAsString("musicmisc", "playnowjumpto", "nowPlayingAlways").StartsWith("nowPlaying");
      }
    }

    private static void StartListeningForEvents()
    {
      _deviceMonitor = new DeviceVolumeMonitor(_windowHandle);
      _deviceMonitor.OnVolumeInserted += new DeviceVolumeAction(VolumeInserted);
      _deviceMonitor.OnVolumeRemoved += new DeviceVolumeAction(VolumeRemoved);
      _deviceMonitor.AsynchronousEvents = true;
      _deviceMonitor.Enabled = true;
    }

    #endregion

    #region cleanup

    private static void StopListeningForEvents()
    {
      if (_deviceMonitor != null)
      {
        _deviceMonitor.Enabled = false;
        _deviceMonitor.OnVolumeInserted -= new DeviceVolumeAction(VolumeInserted);
        _deviceMonitor.OnVolumeRemoved -= new DeviceVolumeAction(VolumeRemoved);
        _deviceMonitor.SafeDispose();
      }
      _deviceMonitor = null;
    }

    #endregion

    #region capture events

    /// <summary>
    /// The event that gets triggered whenever  CD/DVD is removed from a drive.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static void CDRemoved(string DriveLetter)
    {
      Log.Info("Media removed from drive {0}", DriveLetter);
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_CD_REMOVED,
                                      (int)GUIWindow.Window.WINDOW_MUSIC_FILES,
                                      GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
      msg.Label = String.Format("{0}", DriveLetter);
      msg.SendToTargetWindow = true;
      GUIWindowManager.SendThreadMessage(msg);

      msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_CD_REMOVED,
                           (int)GUIWindow.Window.WINDOW_VIDEOS,
                           GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
      msg.Label = DriveLetter;
      msg.SendToTargetWindow = true;
      GUIWindowManager.SendThreadMessage(msg);
    }

    /// <summary>
    /// The event that gets triggered whenever  CD/DVD is inserted into a drive.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static void CDInserted(string DriveLetter)
    {
      Log.Info("Media inserted into drive {0}", DriveLetter);
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_CD_INSERTED,
                                      (int)0,
                                      GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
      msg.Label = DriveLetter;
      GUIWindowManager.SendThreadMessage(msg);
    }

    /// <summary>
    /// The event that gets triggered whenever a new volume is removed.
    /// </summary>	
    private static void VolumeRemoved(int bitMask)
    {
      string driveLetter = _deviceMonitor.MaskToLogicalPaths(bitMask);

      Log.Debug("Volume removed from drive {0}", driveLetter);
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_VOLUME_REMOVED,
                                      (int)0,
                                      GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
      msg.Label = driveLetter;
      GUIWindowManager.SendThreadMessage(msg);
      CDRemoved(driveLetter);
    }

    /// <summary>
    /// The event that gets triggered whenever a new volume is inserted.
    /// </summary>	
    private static void VolumeInserted(int bitMask)
    {
      string driveLetter = _deviceMonitor.MaskToLogicalPaths(bitMask);

      Log.Debug("Volume inserted into drive {0}", driveLetter);
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_VOLUME_INSERTED,
                                      (int)0,
                                      GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
      msg.Label = driveLetter;
      GUIWindowManager.SendThreadMessage(msg);
      if (Util.Utils.IsDVD(driveLetter))
        CDInserted(driveLetter);
    }

    private static bool ShouldWeAutoPlay(MediaType iMedia)
    {
      Log.Info("Check if we want to autoplay a {0}", iMedia);
      if (GUIWindowManager.IsRouted)
      {
        return false;
      }
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_ASKYESNO, 0, 0, 0, 0, 0, null);
      msg.Param1 = 713;
      switch (iMedia)
      {
        case MediaType.PHOTOS: // Photo
          msg.Param2 = 530;
          break;
        case MediaType.VIDEOS: // Movie
          msg.Param2 = 531;
          break;
        case MediaType.AUDIO: // Audio
          msg.Param2 = 532;
          break;
        default:
          msg.Param2 = 714;
          break;
      }
      msg.Param3 = 0;
      GUIWindowManager.SendMessage(msg);
      if (msg.Param1 != 0)
      {
        //stop tv...
        msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_RECORDER_STOP_TV, 0, 0, 0, 0, 0, null);
        GUIWindowManager.SendMessage(msg);

        //stop radio...
        msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_RECORDER_STOP_RADIO, 0, 0, 0, 0, 0, null);
        GUIWindowManager.SendMessage(msg);
        return true;
      }
      return false;
    }

    public static void ExamineCD(string strDrive)
    {
      ExamineCD(strDrive, false);
    }

    public static void ExamineCD(string strDrive, bool forcePlay)
    {
      if (string.IsNullOrEmpty(strDrive) || (g_Player.Playing && DaemonTools.GetVirtualDrive().StartsWith(strDrive)))
      {
        return;
      }

      StopListening();

      GUIMessage msg;
      bool shouldPlay = false;
      switch (DetectMediaType(strDrive))
      {
        case MediaType.AUDIO_CD:
          Log.Info("Audio CD inserted into drive {0}", strDrive);
          //m_audiocd tells us if we want to autoplay or not
          if (forcePlay || m_audiocd == "Yes")
          {
            // Automatically play the CD
            shouldPlay = true;
            Log.Info("CD Autoplay = auto");
          }
          else if (m_audiocd == "Ask")
          {
            if (ShouldWeAutoPlay(MediaType.AUDIO_CD))
            {
              shouldPlay = true;
              Log.Info("CD Autoplay, answered yes");
            }
            else
            {
              Log.Info("CD Autoplay, answered no");
            }
          }
          if (shouldPlay)
          {
            // Send a message with the drive to the message handler. 
            // The message handler will play the CD
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_PLAY_AUDIO_CD,
                                 (int)GUIWindow.Window.WINDOW_MUSIC_FILES,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
            if (m_nowPlayingScreen)
              GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_MUSIC_PLAYING_NOW);
          }
          break;

        case MediaType.PHOTOS:
          if (forcePlay || ShouldWeAutoPlay(MediaType.PHOTOS))
          {
            Log.Info("Media with photo's inserted into drive {0}", strDrive);
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_PICTURES);
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_AUTOPLAY_VOLUME,
                                 (int)GUIWindow.Window.WINDOW_PICTURES,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
          }
          break;

        case MediaType.BLURAY:
          Log.Info("BLU-RAY volume inserted {0}", strDrive);
          GUIMessage msgBluray = new GUIMessage(GUIMessage.MessageType.GUI_MSG_BLURAY_DISK_INSERTED, 0, 0, 0, 0, 0, null);
          msgBluray.Label = strDrive;
          GUIGraphicsContext.SendMessage(msgBluray);
          break;

        case MediaType.HDDVD:
          Log.Info("HD DVD volume inserted {0}", strDrive);
          GUIMessage msgHDDVD = new GUIMessage(GUIMessage.MessageType.GUI_MSG_HDDVD_DISK_INSERTED, 0, 0, 0, 0, 0, null);
          msgHDDVD.Label = strDrive;
          GUIGraphicsContext.SendMessage(msgHDDVD);
          break;

        case MediaType.DVD:
          Log.Info("DVD volume inserted {0}", strDrive);
          if (forcePlay || m_dvd == "Yes")
          {
            Log.Info("Autoplay: Yes, start DVD in {0}", strDrive);
            shouldPlay = true;
          }
          else if (m_dvd == "Ask")
          {
            if (ShouldWeAutoPlay(MediaType.DVD))
            {
              Log.Info("Autoplay: Answered yes, start DVD in {0}", strDrive);
              shouldPlay = true;
            }
            else
            {
              Log.Info("Autoplay: Answered no, do not start DVD in {0}", strDrive);
            }
          }
          if (shouldPlay)
          {
            // Send a message with the drive to the message handler. 
            // The message handler will play the DVD
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_PLAY_DVD,
                                 (int)GUIWindow.Window.WINDOW_VIDEOS,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
          }
          break;

        case MediaType.VCD:
          Log.Info("VCD volume inserted {0}", strDrive);
          if (forcePlay || m_dvd == "Yes")
          {
            Log.Info("Autoplay: Yes, start VCD in {0}", strDrive);
            shouldPlay = true;
          }
          else if (m_dvd == "Ask")
          {
            if (ShouldWeAutoPlay(MediaType.DVD))
            {
              Log.Info("Autoplay: Answered yes, start VCD in {0}", strDrive);
              shouldPlay = true;
            }
            else
            {
              Log.Info("Autoplay: Answered no, do not start VCD in {0}", strDrive);
            }
          }
          if (shouldPlay)
          {
            long lMaxLength = 0;
            string sPlayFile = "";
            string[] files = Directory.GetFiles(strDrive + "\\MPEGAV");
            foreach (string file in files)
            {
              FileInfo info = new FileInfo(file);
              if (info.Length > lMaxLength)
              {
                lMaxLength = info.Length;
                sPlayFile = file;
              }
            }
            GUIGraphicsContext.IsFullScreenVideo = true;
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_FULLSCREEN_VIDEO);
            g_Player.Play(sPlayFile);
          }
          break;

        case MediaType.VIDEOS:
          Log.Info("Video volume inserted {0}", strDrive);
          if (forcePlay || ShouldWeAutoPlay(MediaType.VIDEOS))
          {
            PlayFiles(MediaType.VIDEOS);
          }
          break;

        case MediaType.AUDIO:
          Log.Info("Media with audio inserted into drive {0}", strDrive);
          if (forcePlay || m_audiocd == "Yes")
          {
            // Automatically play the CD
            shouldPlay = true;
            Log.Debug("Adding all Audio Files to Playlist");                        
          }
          else if (m_audiocd == "Ask")
          {
            if (ShouldWeAutoPlay(MediaType.AUDIO))
            {
              shouldPlay = true;
              Log.Info("Audio Autoplay, answered yes");
            }
            else
            {
              Log.Info("Audio Autoplay, answered no");
            }
          }
          if (shouldPlay)
          {
            PlayFiles(MediaType.AUDIO);
          }
          break;

        default:
          Log.Info("Unknown media type inserted into drive {0}", strDrive);
          break;
      }

      StartListening();
    }

    public static void ExamineVolume(string strDrive)
    {
      if (string.IsNullOrEmpty(strDrive) || Util.Utils.IsDVD(strDrive))
      {
        return;
      }

      GUIMessage msg;
      switch (DetectMediaType(strDrive))
      {
        case MediaType.PHOTOS:
          Log.Info("Photo volume inserted {0}", strDrive);
          if (ShouldWeAutoPlay(MediaType.PHOTOS))
          {
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_PICTURES);
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SHOW_DIRECTORY,
                                 (int)GUIWindow.Window.WINDOW_PICTURES,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
          }
          break;

        case MediaType.VIDEOS:
          Log.Info("Video volume inserted {0}", strDrive);
          if (ShouldWeAutoPlay(MediaType.VIDEOS))
          {
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_VIDEOS);
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SHOW_DIRECTORY,
                                 (int)GUIWindow.Window.WINDOW_VIDEOS,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
          }
          break;

        case MediaType.AUDIO:
          Log.Info("Audio volume inserted {0}", strDrive);
          if (ShouldWeAutoPlay(MediaType.AUDIO))
          {
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_MUSIC_FILES);
            msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SHOW_DIRECTORY,
                                 (int)GUIWindow.Window.WINDOW_MUSIC_FILES,
                                 GUIWindowManager.ActiveWindow, 0, 0, 0, 0);
            msg.Label = strDrive;
            msg.SendToTargetWindow = true;
            GUIWindowManager.SendThreadMessage(msg);
          }
          break;

        default:
          Log.Info("ExamineVolume: Unknown media type inserted into drive {0}", strDrive);
          break;
      }
    }
    
    private static MediaType GetMediaTypeFromFiles(string strFolder)
    {
      if (string.IsNullOrEmpty(strFolder))
      {
        return MediaType.UNKNOWN;
      }
      try
      {
        allfiles.Clear();
        MediaType mediaTypeFound = MediaType.UNKNOWN;                
        string[] files = Directory.GetFiles(strFolder, "*.*", SearchOption.AllDirectories);
        if (files != null && files.Length > 0)
        {
          for (int i = files.Length -1; i >= 0; i--)
          {
            if (Util.Utils.IsVideo(files[i]))
            {
              mediaTypeFound = MediaType.VIDEOS;
            }
            else if (mediaTypeFound != MediaType.VIDEOS && Util.Utils.IsAudio(files[i]))
            {
              mediaTypeFound = MediaType.AUDIO;
              if (Path.GetExtension(files[i]).ToLower() == ".cda")
                mediaTypeFound = MediaType.AUDIO_CD;
            }
            else if (mediaTypeFound == MediaType.UNKNOWN && Util.Utils.IsPicture(files[i]))
            {
              mediaTypeFound = MediaType.PHOTOS;
            }
            allfiles.Add(files[i]);
          }
        }
        return mediaTypeFound;
      }
      catch (Exception) {}
      return MediaType.UNKNOWN;
    }

    /// <summary>
    /// Detects the media type of the CD/DVD inserted into a drive.
    /// </summary>
    /// <param name="driveLetter">The drive that contains the data.</param>
    /// <returns>The media type of the drive.</returns>
    private static MediaType DetectMediaType(string strDrive)
    {
      if (string.IsNullOrEmpty(strDrive))
      {
        return MediaType.UNKNOWN;
      }
      try
      {
        if (Directory.Exists(strDrive + "\\VIDEO_TS"))
        {
          return MediaType.DVD;
        }

        if (File.Exists(strDrive + "\\BDMV\\index.bdmv"))
        {
          return MediaType.BLURAY;
        }

        if (Directory.Exists(strDrive + "\\HVDVD_TS"))
        {
          return MediaType.HDDVD;
        }

        if (Directory.Exists(strDrive + "\\MPEGAV"))
        {
          return MediaType.VCD;
        }
        
        return GetMediaTypeFromFiles(strDrive + "\\");        
      }
      catch (Exception) { }
      return MediaType.UNKNOWN;
    }

    private static void PlayFiles(MediaType mediaType)
    {
      bool startPlaylist = false;
      PlayListPlayer playlistPlayer = PlayListPlayer.SingletonPlayer;

      if (mediaType == MediaType.AUDIO)
        playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_MUSIC).Clear();
      else
        playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_VIDEO).Clear();

      foreach (string file in allfiles)
      {
        if (mediaType == MediaType.AUDIO && !Util.Utils.IsAudio(file))
          continue;
        else if (mediaType == MediaType.VIDEOS && !Util.Utils.IsVideo(file))
          continue;

        PlayListItem item = new PlayListItem();
        item.FileName = file;
        if (mediaType == MediaType.AUDIO)
        {
          item.Type = PlayListItem.PlayListItemType.Audio;
          playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_MUSIC).Add(item);
        }
        else
        {
          item.Type = PlayListItem.PlayListItemType.Video;
          item.Description = file;
          playlistPlayer.GetPlaylist(PlayListType.PLAYLIST_VIDEO).Add(item); 
        }
        startPlaylist = true;        
      }
      if (startPlaylist)
      {        
        Log.Debug("Start playing Playlist");
        playlistPlayer.Reset();

        if (mediaType == MediaType.AUDIO)
        {
          playlistPlayer.CurrentPlaylistType = PlayListType.PLAYLIST_MUSIC;
          GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_MUSIC_PLAYLIST);
          if (m_nowPlayingScreen)
            GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_MUSIC_PLAYING_NOW);          
        }
        else
        {
          playlistPlayer.CurrentPlaylistType = PlayListType.PLAYLIST_VIDEO;
          GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_VIDEO_PLAYLIST);
        }        
        playlistPlayer.Play(0);
      }
    }

    #endregion
  }
}