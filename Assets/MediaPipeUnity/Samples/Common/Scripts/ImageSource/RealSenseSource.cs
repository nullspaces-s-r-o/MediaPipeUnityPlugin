// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using Intel.RealSense;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Mediapipe.Unity
{
  public interface IColorSource
  {
    public struct Intrin
    {
      public float width;
      public float height;
      public float ppx;
      public float ppy;
      public float fx;
      public float fy;
    }

    Intrin GetIntrin();
  }
  public interface IDepthSource
  {
    Texture2D GetDepthTexture();
  }

  public class RealSenseSource : ImageSource, IDepthSource, IColorSource
  {
    public Intel.RealSense.Pipeline pipeline;

    public override string sourceName => "RealSense";

    private readonly int _preferableDefaultWidth = 1280;

    private const string _TAG = nameof(RealSenseSource);

    private readonly ResolutionStruct[] _defaultAvailableResolutions;

    public RealSenseSource(int preferableDefaultWidth, ResolutionStruct[] defaultAvailableResolutions)
    {
      _preferableDefaultWidth = preferableDefaultWidth;
      _defaultAvailableResolutions = defaultAvailableResolutions;
    }

    private static readonly object _PermissionLock = new object();
    private static bool _IsPermitted = false;

    public override int textureWidth => 1280;
    public override int textureHeight => 720;

    public override bool isVerticallyFlipped => true;
    public override bool isFrontFacing => false;
    public override RotationAngle rotation => RotationAngle.Rotation0;


    //public override string[] sourceCandidateNames => availableSources?.Select(device => device.name).ToArray();
    public override string[] sourceCandidateNames
    {
      get
      {
        var names = new List<string>();
        var context = new Intel.RealSense.Context();
        foreach (var device in context.QueryDevices())
        {
          var name = device.Info[Intel.RealSense.CameraInfo.Name];
          Debug.Log("Device: " + name);
          names.Add(name);
        }

        return names.ToArray();
      }
    }

#pragma warning disable IDE0025
    public override ResolutionStruct[] availableResolutions
    {
      get
      {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (webCamDevice is WebCamDevice valueOfWebCamDevice) {
          return valueOfWebCamDevice.availableResolutions.Select(resolution => new ResolutionStruct(resolution)).ToArray();
        }
#endif
        return new ResolutionStruct[] { new ResolutionStruct(1280, 720, 30) };
      }
    }
#pragma warning restore IDE0025

    private bool rs_isPrepared = false;

    public override bool isPrepared => rs_isPrepared;
    public override bool isPlaying => rs_isPrepared;

    private IEnumerator Initialize()
    {
      yield return GetPermission();

      if (!_IsPermitted)
      {
        yield break;
      }


      yield return null;
    }

    private IEnumerator GetPermission()
    {
      lock (_PermissionLock)
      {
        if (_IsPermitted)
        {
          yield break;
        }

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
          Permission.RequestUserPermission(Permission.Camera);
          yield return new WaitForSeconds(0.1f);
        }
#elif UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) {
          yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }
#endif

#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
          Debug.LogWarning("Not permitted to use Camera");
          yield break;
        }
#elif UNITY_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) {
          Debug.LogWarning("Not permitted to use WebCam");
          yield break;
        }
#endif
        _IsPermitted = true;

        yield return new WaitForEndOfFrame();
      }
    }

    public override void SelectSource(int sourceId)
    {
      if (sourceId < 0 || sourceId >= 1)
      {
        throw new ArgumentException($"Invalid source ID: {sourceId}");
      }
    }

    public override IEnumerator Play()
    {
      //yield return Initialize();
      //if (!_IsPermitted)
      //{
      //  throw new InvalidOperationException("Not permitted to access cameras");
      //}

      //InitializeWebCamTexture();
      //webCamTexture.Play();
      //yield return WaitForWebCamTexture();


      rs_isPrepared = true;

      pipeline = new Intel.RealSense.Pipeline();
      var config = new Intel.RealSense.Config();
      config.EnableStream(Intel.RealSense.Stream.Color, 1280, 720, Intel.RealSense.Format.Rgb8, 30);
      config.EnableStream(Intel.RealSense.Stream.Depth, 1280, 720);

      pipeline.Start(config);


      yield return null;
    }

    public override IEnumerator Resume()
    {
      //if (!isPrepared)
      //{
      //  throw new InvalidOperationException("WebCamTexture is not prepared yet");
      //}
      //if (!webCamTexture.isPlaying)
      //{
      //  webCamTexture.Play();
      //}
      //yield return WaitForWebCamTexture();
      yield return null;
    }

    public override void Pause()
    {
      //if (isPlaying)
      //{
      //  webCamTexture.Pause();
      //}
    }

    public override void Stop()
    {
      if (pipeline != null)
      {
        pipeline.Stop();
        pipeline.Dispose();
        pipeline = null;
      }
    }

    private Texture2D texture = null;
    private Texture2D depthTexture = null;
    private Intel.RealSense.Align align = new Intel.RealSense.Align(Intel.RealSense.Stream.Color);

    public override Texture GetCurrentTexture()
    {
      if (pipeline != null)
      {
        var frames = pipeline.WaitForFrames();
        var alignedFrame = align.Process(frames);
        var alignedFrameset = alignedFrame.As<FrameSet>();

        var colorFrame = frames.ColorFrame;
        if (texture == null)
        {
          texture = new Texture2D(colorFrame.Width, colorFrame.Height, TextureFormat.RGB24, false, true);

          var intrin = colorFrame.Profile.As<VideoStreamProfile>().GetIntrinsics();
          colorIntrin.width = intrin.width;
          colorIntrin.height = intrin.height;
          colorIntrin.ppx = intrin.ppx;
          colorIntrin.ppy = intrin.ppy;
          colorIntrin.fx = intrin.fx;
          colorIntrin.fy = intrin.fy;

        }
        texture.LoadRawTextureData(colorFrame.Data, colorFrame.Stride * colorFrame.Height);
        texture.Apply();
        colorFrame.Dispose();


        var depthFrame = alignedFrameset.DepthFrame;
        if (depthTexture == null)
        {
          depthTexture = new Texture2D(depthFrame.Width, depthFrame.Height, TextureFormat.R16, false, true);
        }
        depthTexture.LoadRawTextureData(depthFrame.Data, depthFrame.Stride * depthFrame.Height);
        depthFrame.Dispose();

        alignedFrame.Dispose();
        alignedFrameset.Dispose();
        frames.Dispose(); // https://github.com/IntelRealSense/librealsense/blob/master/wrappers/csharp/Documentation/pinvoke.md
      }

      return texture;
      //return webCamTexture;
    }

    // deproject pixel to point usin intel realsense api


    private ResolutionStruct GetDefaultResolution()
    {
      var resolutions = availableResolutions;
      return resolutions == null || resolutions.Length == 0 ? new ResolutionStruct() : resolutions.OrderBy(resolution => resolution, new ResolutionStructComparer(_preferableDefaultWidth)).First();
    }

    public Texture2D GetDepthTexture() => depthTexture;

    IColorSource.Intrin colorIntrin;

    public IColorSource.Intrin GetIntrin() {
      return colorIntrin;
    }

    //private void InitializeWebCamTexture()
    //{
    //  Stop();
    //  if (webCamDevice is WebCamDevice valueOfWebCamDevice)
    //  {
    //    webCamTexture = new WebCamTexture(valueOfWebCamDevice.name, resolution.width, resolution.height, (int)resolution.frameRate);
    //    return;
    //  }
    //  throw new InvalidOperationException("Cannot initialize WebCamTexture because WebCamDevice is not selected");
    //}

    //private IEnumerator WaitForWebCamTexture()
    //{
    //  const int timeoutFrame = 2000;
    //  var count = 0;
    //  Debug.Log("Waiting for WebCamTexture to start");
    //  yield return new WaitUntil(() => count++ > timeoutFrame || webCamTexture.width > 16);

    //  if (webCamTexture.width <= 16)
    //  {
    //    throw new TimeoutException("Failed to start WebCam");
    //  }
    //}

    private class ResolutionStructComparer : IComparer<ResolutionStruct>
    {
      private readonly int _preferableDefaultWidth;

      public ResolutionStructComparer(int preferableDefaultWidth)
      {
        _preferableDefaultWidth = preferableDefaultWidth;
      }

      public int Compare(ResolutionStruct a, ResolutionStruct b)
      {
        var aDiff = Mathf.Abs(a.width - _preferableDefaultWidth);
        var bDiff = Mathf.Abs(b.width - _preferableDefaultWidth);
        if (aDiff != bDiff)
        {
          return aDiff - bDiff;
        }
        if (a.height != b.height)
        {
          // prefer smaller height
          return a.height - b.height;
        }
        // prefer smaller frame rate
        return (int)(a.frameRate - b.frameRate);
      }
    }
  }
}
