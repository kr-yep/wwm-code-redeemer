using System;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WwmRedeemer {
 public static class Ocr {
  static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> operation) {
   var deadline = DateTime.UtcNow.AddSeconds(15);
   while (operation.Status == Windows.Foundation.AsyncStatus.Started) {
    if (DateTime.UtcNow > deadline) { operation.Cancel(); throw new TimeoutException("OCR 逾時"); }
    await Task.Delay(25);
   }
   try { return operation.GetResults(); } finally { operation.Close(); }
  }
  public static byte[] Contrast(byte[] png) {
   using(var input=new MemoryStream(png))using(var original=new Bitmap(input))using(var b=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb)) {
    using(var g=Graphics.FromImage(b))g.DrawImageUnscaled(original,0,0);
    var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
    byte[] bytes=new byte[data.Stride*b.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
    for(int i=0;i<bytes.Length;i+=4){byte v=(bytes[i]+bytes[i+1]+bytes[i+2])/3>170?(byte)0:(byte)255;bytes[i]=bytes[i+1]=bytes[i+2]=v;bytes[i+3]=255;}
    Marshal.Copy(bytes,0,data.Scan0,bytes.Length);b.UnlockBits(data);
    using(var output=new MemoryStream()){b.Save(output,ImageFormat.Png);return output.ToArray();}
   }
  }
  public static byte[] Scale(byte[] png,int factor){
   using(var input=new MemoryStream(png))using(var source=new Bitmap(input))using(var outputImage=new Bitmap(source.Width*factor,source.Height*factor)){
    using(var graphics=Graphics.FromImage(outputImage)){graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;graphics.DrawImage(source,0,0,outputImage.Width,outputImage.Height);}
    using(var output=new MemoryStream()){outputImage.Save(output,ImageFormat.Png);return output.ToArray();}
   }
  }
  public static async Task<OcrResult> Read(byte[] png,string language="zh-Hant") {
   var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(language)) ?? OcrEngine.TryCreateFromUserProfileLanguages();
   if (engine == null) throw new InvalidOperationException("Windows 沒有可用的 OCR 語言。請先安裝繁體中文 OCR 語言功能。");
   using (var stream = new InMemoryRandomAccessStream()) {
    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await Wait<uint>(writer.StoreAsync()); await Wait<bool>(writer.FlushAsync()); }
    stream.Seek(0);
    var decoder = await Wait<BitmapDecoder>(BitmapDecoder.CreateAsync(stream));
    using (var bitmap = await Wait<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))) {
     if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension) throw new InvalidOperationException("截圖尺寸超過 OCR 限制。");
     return await Wait<OcrResult>(engine.RecognizeAsync(bitmap));
    }
   }
  }
 }
}
