using System;
using Android;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Lang;
using PhototasticAndroid;
using Orientation = Android.Content.Res.Orientation;

namespace SurfaceCamera
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private Android.Hardware.Camera _camera;
        private int _cameraId;
        private CameraPreview _camPreview;

        public static readonly string[] CameraPerms =
        {
            Manifest.Permission.Camera,
            Manifest.Permission.WriteExternalStorage
        };

        public const int RequestCameraId = 1001;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);
            if (SetCameraInstance() == true)
            {
                _camPreview = new CameraPreview(this, _camera, _cameraId);
            }
            else
            {
                Finish();
            }

            var preview = (RelativeLayout)FindViewById(Resource.Id.preview_layout);
            preview.AddView(_camPreview);

            var previewLayout = (RelativeLayout.LayoutParams)_camPreview.LayoutParameters;
            previewLayout.Width = ViewGroup.LayoutParams.MatchParent;
            previewLayout.Height = ViewGroup.LayoutParams.MatchParent;
            _camPreview.LayoutParameters = previewLayout;

            var captureButton = (Button)FindViewById(Resource.Id.button_capture);
            captureButton.Click += (s, e) =>
            {
                _camera.TakePicture(null, null, _camPreview);
            };

            // at last, a call to set the right layout of the elements (like the button)
            // depending on the screen orientation (if it's changeable).
            FixElementsPosition(Resources.Configuration.Orientation);
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (SetCameraInstance() == true)
            {
                //todo _camPreview.Refresh.....
            }
            else
            {
                Finish();
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            ReleaseCameraInstance();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseCameraInstance();
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            FixElementsPosition(newConfig.Orientation);
        }

        private bool SetCameraInstance()
        {
            if (_camera != null)
            {
                return true;
            }

            if ((int)Build.VERSION.SdkInt >= 9)
            {
                if (_cameraId < 0)
                {
                    var camInfo = new Android.Hardware.Camera.CameraInfo();
                    for (int i = 0; i < Android.Hardware.Camera.NumberOfCameras; i++)
                    {
                        Android.Hardware.Camera.GetCameraInfo(i, camInfo);

                        if (camInfo.Facing == Android.Hardware.Camera.CameraInfo.CameraFacingBack)
                        {
                            try
                            {
                                _camera = Android.Hardware.Camera.Open(i);
                                _cameraId = i;
                                return true;
                            }
                            catch (RuntimeException e)
                            {
                                Console.WriteLine(e);
                            }
                        }
                    }
                }

                else
                {
                    try
                    {
                        _camera = Android.Hardware.Camera.Open(_cameraId);
                    }
                    catch (RuntimeException e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }

            if (_camera == null)
            {
                try
                {
                    _camera = Android.Hardware.Camera.Open();
                    _cameraId = 0;
                }
                catch (RuntimeException e)
                {
                    // this is REALLY bad, the camera is definitely locked by the system.
                    return false;
                }
            }

            // here, the open() went good and the camera is available
            return true;
        }

        private void ReleaseCameraInstance()
        {
            if (_camera != null)
            {
                try
                {
                    _camera.StopPreview();
                }
                catch (System.Exception e)
                {
                }

                _camera.SetPreviewCallback(null);
                _camera.Release();
                _camera = null;
                _cameraId = -1;
            }
        }

        private void FixElementsPosition(Orientation orientation)
        {
            var captureButton = (Button)FindViewById(Resource.Id.button_capture);
            FrameLayout.LayoutParams layout = (FrameLayout.LayoutParams)captureButton.LayoutParameters;

            switch (orientation)
            {
                case Orientation.Landscape:
                    layout.Gravity = GravityFlags.Right | GravityFlags.Center;
                    break;
                case Orientation.Portrait:
                    layout.Gravity = GravityFlags.Bottom | GravityFlags.Center;
                    break;
            }
        }
    }
}

