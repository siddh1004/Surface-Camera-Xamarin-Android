using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Java.Lang;
using SurfaceCamera;
using Camera = Android.Hardware.Camera;
using Exception = System.Exception;
using Math = System.Math;

namespace PhototasticAndroid
{
    public class CameraPreview : SurfaceView, ISurfaceHolderCallback, Camera.IPreviewCallback, Camera.IPictureCallback
    {
        /**
	 * ASPECT_RATIO_W and ASPECT_RATIO_H define the aspect ratio 
	 * of the Surface. They are used when {@link #onMeasure(int, int)}
	 * is called.
	 */
        private const float AspectRationWidth = 4.0f;
        private const float AspectRationHeight = 3.0f;

        /**
	 * The maximum dimension (in pixels) of the preview frames that are produced 
	 * by the Camera object. Note that this should not be intended as 
	 * the final, exact, dimension because the device could not support 
	 * it and a lower value is required (but the aspect ratio should remain the same).<br />
	 * See {@link CameraPreview#getBestSize(List, int)} for more information.
	 */
        private const int PreviewMaxWidth = 640;

        /**
         * The maximum dimension (in pixels) of the images produced when a 
         * {@link PictureCallback#onPictureTaken(byte[], Camera)} event is
         * fired. Again, this is a maximum value and could not be the 
         * real one implemented by the device.
         */
        private const int PictureMaxWidth = 1280;

        /**
	 * In this example we look at camera preview buffer functionality too.<br />
	 * This is the array that will be filled everytime a single preview frame is 
	 * ready to be processed (for example when we want to show to the user 
	 * a transformed preview instead of the original one, or when we want to 
	 * make some image analysis in real-time without taking full-sized pictures).
	 */
        private byte[] _previewBuffer;

        private ISurfaceHolder _holder;
        private Camera _camera;
        private int _cameraId;

        public CameraPreview(Context context, Camera camera, int cameraId)
            : base(context)
        {
            _camera = camera;
            _cameraId = cameraId;
            _holder = Holder;
            _holder.AddCallback(this);
            Focusable = true;
            FocusableInTouchMode = true;
            _holder.SetType(SurfaceType.PushBuffers);
        }

        public void SurfaceCreated(ISurfaceHolder holder)
        {
            SetupCamera();
            StartCameraPreview(holder);
        }

        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            if (holder.Surface == null)
            {
                return;
            }

            // stop preview before making changes!
            StopCameraPreview();

            // set preview size and make any resize, rotate or
            // reformatting changes here
            UpdateCameraDisplayOrientation();

            // restart preview with new settings
            StartCameraPreview(holder);
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
        }

        /**
	 * [IMPORTANT!] Probably the most important method here. Lots of users experience bad 
	 * camera behaviors because they don't override this guy.
	 * In fact, some Android devices are very strict about the size of the surface 
	 * where the preview is printed: if its ratio is different from the 
	 * original one, it results in errors like "startPreview failed".<br />
	 * This methods takes care on this and applies the right size to the
	 * {@link CameraPreview}.
	 * @param widthMeasureSpec horizontal space requirements as imposed by the parent.
	 * @param heightMeasureSpec vertical space requirements as imposed by the parent.  
	 */
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            int height = MeasureSpec.GetSize(heightMeasureSpec);
            int width = MeasureSpec.GetSize(widthMeasureSpec);

            // do some ultra high precision math...
            float ratio = AspectRationHeight / AspectRationWidth;
            if (width > height * ratio)
            {
                width = (int)(height / ratio + .5);
            }
            else
            {
                height = (int)(width / ratio + .5);
            }

            SetMeasuredDimension(width, height);
        }

        private void SetupCamera()
        {
            // Get the camera object directly from the parent activity
            // This is safe and doesn't throw NullPointerException or "camera has been released"
            // when resuming from a paused state, because it always takes the latest camera instance
            //MainActivity parent = (PhototasticCamera)this.getContext();
            //Camera camera = parent.getCamera();

            if (_camera == null)
            {
               Console.WriteLine("setupCamera(): warning, camera is null");
                return;
            }

            var parameters = _camera.GetParameters();

            var bestPreviewSize = GetBestSize(parameters.SupportedPictureSizes, PreviewMaxWidth);
            var bestPictureSize = GetBestSize(parameters.SupportedPictureSizes, PictureMaxWidth);

            parameters.SetPreviewSize(bestPreviewSize.Width, bestPreviewSize.Height);
            parameters.SetPreviewSize(bestPictureSize.Width, bestPictureSize.Height);

            parameters.PreviewFormat = ImageFormatType.Nv21;  // NV21 is the most supported format for preview frames
            parameters.PictureFormat = ImageFormatType.Jpeg;  // JPEG for full resolution images

            // example of settings
            try
            {
                parameters.FlashMode = Camera.Parameters.FlashModeOff;
            }
            catch (NoSuchMethodError e)
            {
                // remember that not all the devices support a given feature
                Console.WriteLine($"setupCamera(): this camera ignored some unsupported settings. {e}");
            }

            _camera.SetParameters(parameters); // save everything

            // print saved parameters
            int prevWidth = _camera.GetParameters().PreviewSize.Width;
            int prevHeight = _camera.GetParameters().PreviewSize.Height;
            int picWidth = _camera.GetParameters().PictureSize.Width;
            int picHeight = _camera.GetParameters().PictureSize.Height;

            // here: previewBuffer initialization. It will host every frame that comes out
            // from the preview, so it must be big enough.
            // After that, it's linked to the camera with the setCameraCallback() method.
            try
            {
                this._previewBuffer = new byte[prevWidth * prevHeight * ImageFormat.GetBitsPerPixel(_camera.GetParameters().PreviewFormat) / 8];
                SetCameraCallBack();
            }
            catch (IOException e)
            {
                Console.WriteLine($"setupCamera(): error setting camera callback., {e}");
            }
        }

        /**
   * [IMPORTANT!] This is a convenient function to determine what's the proper
   * preview/picture size to be assigned to the camera, by looking at 
   * the list of supported sizes and the maximum value given
   * @param sizes sizes that are currently supported by the camera hardware,
   * retrived with {@link Camera.Parameters#getSupportedPictureSizes()} or {@link Camera.Parameters#getSupportedPreviewSizes()}
   * @param widthThreshold the maximum value we want to apply
   * @return an optimal size <= widthThreshold
   */
        private Camera.Size GetBestSize(IList<Camera.Size> sizes, int widthThreshold)
        {
            Camera.Size bestSize = null;

            foreach (var currentSize in sizes)
            {
                var isDesiredRatio = ((currentSize.Width / AspectRationWidth) == (currentSize.Height / AspectRationHeight));
                var isBetterSize = (bestSize == null || currentSize.Width > bestSize.Width);
                var isInBounds = currentSize.Width <= widthThreshold;

                if (isDesiredRatio && isInBounds && isBetterSize)
                {
                    bestSize = currentSize;
                }
            }

            if (bestSize == null)
            {
                bestSize = sizes[0];
                Console.WriteLine("determineBestSize(): can't find a good size. Setting to the very first...");
            }

            return bestSize;
        }

        /**
    * [IMPORTANT!] Sets the {@link #previewBuffer} to be the default buffer where the 
    * preview frames will be copied. Also sets the callback function 
    * when a frame is ready.
    * @throws IOException
    */
        private void SetCameraCallBack()
        {
            _camera.AddCallbackBuffer(this._previewBuffer);
            _camera.SetPreviewCallbackWithBuffer(this);
        }

        public void OnPreviewFrame(byte[] data, Camera camera)
        {
            ProcessFrame(data, camera);
            camera.AddCallbackBuffer(_previewBuffer);
        }

        private void ProcessFrame(byte[] raw, Camera cam)
        {
            //Todo: insert a good YUV->RGB conversion algorithm?
        }

        /**
	 * In addition to calling {@link Camera#startPreview()}, it also 
	 * updates the preview display that could be changed in some situations
	 * @param holder the current {@link SurfaceHolder}
	 */

        private async void StartCameraPreview(ISurfaceHolder holder)
        {
            try
            {
                _camera.SetPreviewDisplay(holder);
                _camera.StartPreview();
                _camera.AutoFocus(null);
            }
            catch (Exception e)
            {
                Console.WriteLine($"startCameraPreview(): error starting camera preview, {e}");
            }
        }

        /**
	 * It "simply" calls {@link Camera#stopPreview()} and checks
	 * for errors
	 */

        private async void StopCameraPreview()
        {
            try
            {
                _camera.StopPreview();
            }
            catch (Exception e)
            {
                Console.WriteLine("stopCameraPreview(): tried to stop a non-running preview, this is not an error");
            }
        }

        /**
	 * Gets the current screen rotation in order to understand how much 
	 * the surface needs to be rotated
	 */
        private void UpdateCameraDisplayOrientation()
        {
            int cameraID = _cameraId;

            if (_camera == null)
            {
                Console.WriteLine("updateCameraDisplayOrientation(): warning, camera is null");
                return;
            }

            int result = 0;
            MainActivity parentActivity = (MainActivity)Context;

            int rotation = (int)parentActivity.Window.WindowManager.DefaultDisplay.Rotation;
            int degrees = 0;

            switch (rotation)
            {
                case (int)SurfaceOrientation.Rotation0:
                    degrees = 0;
                    break;

                case (int)SurfaceOrientation.Rotation90:
                    degrees = 90;
                    break;

                case (int)SurfaceOrientation.Rotation180:
                    degrees = 180;
                    break;

                case (int)SurfaceOrientation.Rotation270:
                    degrees = 270;
                    break;
            }

            if ((int)Build.VERSION.SdkInt >= 9)
            {
                // on >= API 9 we can proceed with the CameraInfo method
                // and also we have to keep in mind that the camera could be the front one 
                Camera.CameraInfo info = new Camera.CameraInfo();
                Camera.GetCameraInfo(cameraID, info);

                if (info.Facing == Camera.CameraInfo.CameraFacingFront)
                {
                    result = (info.Orientation + degrees) % 360;
                    result = (360 - result) % 360;  // compensate the mirror
                }
                else
                {
                    // back-facing
                    result = (info.Orientation - degrees + 360) % 360;
                }
            }
            else
            {
                // TODO: on the majority of API 8 devices, this trick works good 
                // and doesn't produce an upside-down preview.
                // ... but there is a small amount of devices that don't like it!
                result = Math.Abs(degrees - 90);
            }

            _camera.SetDisplayOrientation(result); // save settings
        }

        public void OnPictureTaken(byte[] data, Camera camera)
        {
            StopCameraPreview(); // better do that because we don't need a preview right now

            // create a Bitmap from the raw data
            Bitmap picture = BitmapFactory.DecodeByteArray(data, 0, data.Length);


            Activity parentActivity = (Activity)this.Context;
            int rotation = (int)parentActivity.Window.WindowManager.DefaultDisplay.Rotation;
            if (rotation == (int)SurfaceOrientation.Rotation0 || rotation == (int)SurfaceOrientation.Rotation180)
            {
                Matrix matrix = new Matrix();
                matrix.PostRotate(90);
                // create a rotated version and replace the original bitmap
                picture = Bitmap.CreateBitmap(picture, 0, 0, picture.Width, picture.Height, matrix, true);
            }

            // save to media library
            MediaStore.Images.Media.InsertImage(parentActivity.ContentResolver, picture, $"{Guid.NewGuid()}", string.Empty);

            // show a message
            Toast toast = Toast.MakeText(parentActivity, "Picture saved to the media library", ToastLength.Long);
            toast.Show();

            StartCameraPreview(Holder);
        }
    }
}