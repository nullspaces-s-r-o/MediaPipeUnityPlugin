// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System;
using System.Collections;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
  public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
  {
    [SerializeField] private HandLandmarkerResultAnnotationController _handLandmarkerResultAnnotationController;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"NumHands = {config.NumHands}");
      Debug.Log($"MinHandDetectionConfidence = {config.MinHandDetectionConfidence}");
      Debug.Log($"MinHandPresenceConfidence = {config.MinHandPresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetHandLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnHandLandmarkDetectionOutput : null);
      taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_handLandmarkerResultAnnotationController, imageSource);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = HandLandmarkerResult.Alloc(options.numHands);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      var tStart = DateTime.Now;
      int nframes = 0;

      // create new array of 3D hand landmarks programically
      var gameObjects = new GameObject[21];
      for (int i = 0; i < 21; i++)
      {
        gameObjects[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        gameObjects[i].transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        gameObjects[i].transform.transform.position = new Vector3(i, 0, 0);
      }

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              _handLandmarkerResultAnnotationController.DrawNow(result);

              if (imageSource is IDepthSource depthSource)
              {
                var depthTexture = depthSource.GetDepthTexture();
                if (imageSource is IColorSource colorSource)
                {
                  var colorIntrin = colorSource.GetIntrin();

                  // de-normalize the hand landmarks, for single hand now
                  var handLandmarks = result.handLandmarks[0];

                  var origin = new Vector3();
                  for (int i = 0; i < 21; i++)
                  {
                    var wristNormalized = handLandmarks.landmarks[i];
                    var nx = wristNormalized.x;
                    var ny = wristNormalized.y;

                    var x = nx * colorIntrin.width;
                    var y = ny * colorIntrin.height;

                    if (0 <= x && x < colorIntrin.width && 0 <= y && y < colorIntrin.height)
                    {
                      var vx = (x - colorIntrin.ppx) / colorIntrin.fx;
                      var vy = (y - colorIntrin.ppy) / colorIntrin.fy;

                      if (i == 0)
                      {
                        // Get the raw texture data
                        byte[] rawData = depthTexture.GetRawTextureData();

                        // Calculate the index of the pixel
                        int index = (int)y * depthTexture.width * 2 + (int)x * 2;

                        // Convert the byte data to ushort
                        ushort pixelValue = BitConverter.ToUInt16(rawData, index);

                        var vz = pixelValue * 0.001f;

                        origin = new Vector3(vx, vy, vz);
                      }

                      // Locate 'wrist' game object
                      var lm = gameObjects[i];
                      if (lm)
                      {
                        if (i == 0)
                        {
                          lm.transform.position = origin;
                        }
                        else
                        {
                          lm.transform.position = new Vector3(vx, vy, origin.z + wristNormalized.z);
                        }
                      }
                      else
                      {
                        Debug.LogWarning("Wrist game object not found");
                      }

                      //Debug.Log($"Wrist: {vx}, {vy}, {vz}");
                    }
                  }
                }
              }
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _handLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _handLandmarkerResultAnnotationController.DrawNow(default);
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
          default:
            break;
        }

        var tEnd = DateTime.Now;
        var elapsed = tEnd - tStart;

        ++nframes;

        if (elapsed.TotalSeconds > 5.0)
        {
          Debug.Log($"FPS: {nframes / elapsed.TotalSeconds}");
          tStart = tEnd;
          nframes = 0;
        }
      }
    }

    private void OnHandLandmarkDetectionOutput(HandLandmarkerResult result, Image image, long timestamp)
    {
      _handLandmarkerResultAnnotationController.DrawLater(result);
    }
  }
}
