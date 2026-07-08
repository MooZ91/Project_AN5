using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class ScreenRecorder : MonoBehaviour
{
    [Header("UI")]
    public Button grabarButton;
    public Text buttonText;

    [Header("Recording")]
    [Tooltip("Frames per second to capture")]
    public int frameRate = 30;
    [Tooltip("JPEG quality 1-100")]
    [Range(1, 100)]
    public int jpegQuality = 90;

    private bool isRecording = false;
    private MjpegAviWriter aviWriter;
    private string filePath;
    private int frameIndex = 0;
    private Color normalColor;
    private readonly Color recordingColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    private string originalLabel;
    private Coroutine captureCoroutine;

    void Start()
    {
        if (grabarButton == null)
            grabarButton = GetComponent<Button>();

        if (grabarButton != null)
        {
            var img = grabarButton.GetComponent<Image>();
            if (img != null) normalColor = img.color;
            grabarButton.onClick.AddListener(ToggleRecording);
        }

        if (buttonText != null)
            originalLabel = buttonText.text;
    }

    public void ToggleRecording()
    {
        if (!isRecording)
            StartRecording();
        else
            StopRecording();
    }

    private void StartRecording()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "recordings"));
        Directory.CreateDirectory(dir);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filePath = Path.Combine(dir, $"recording_{timestamp}.avi");

        aviWriter = new MjpegAviWriter(filePath, Screen.width, Screen.height, frameRate);
        frameIndex = 0;
        isRecording = true;

        var img = grabarButton?.GetComponent<Image>();
        if (img != null) img.color = recordingColor;
        if (buttonText != null) buttonText.text = "■ DETENER";

        captureCoroutine = StartCoroutine(CaptureFrames());
        Debug.Log($"[ScreenRecorder] Grabación iniciada: {filePath}");
    }

    private void StopRecording()
    {
        isRecording = false;

        if (captureCoroutine != null)
        {
            StopCoroutine(captureCoroutine);
            captureCoroutine = null;
        }

        aviWriter?.Close();
        aviWriter = null;

        var img = grabarButton?.GetComponent<Image>();
        if (img != null) img.color = normalColor;
        if (buttonText != null) buttonText.text = originalLabel;

        Debug.Log($"[ScreenRecorder] Grabación detenida. {frameIndex} fotogramas. Archivo: {filePath}");
    }

    private IEnumerator CaptureFrames()
    {
        float interval = 1f / frameRate;
        float nextCapture = Time.unscaledTime;

        while (isRecording)
        {
            yield return new WaitForEndOfFrame();

            if (Time.unscaledTime >= nextCapture)
            {
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] jpeg = frame.EncodeToJPG(jpegQuality);
                Destroy(frame);
                aviWriter.AddFrame(jpeg);
                frameIndex++;
                nextCapture += interval;
            }
        }
    }

    private void OnDestroy()
    {
        if (isRecording)
            StopRecording();
    }
}

// Writes a Motion-JPEG AVI file playable by VLC, Windows Media Player, etc.
public class MjpegAviWriter
{
    private FileStream fs;
    private BinaryWriter bw;
    private int width, height, fps;
    private List<(long offset, int size)> index = new List<(long, int)>();

    private long riffSizePos;
    private long avihTotalFramesPos;
    private long avihMaxBytesPos;
    private long strhLengthPos;
    private long moviListSizePos;
    private long moviDataStart; // position right after 'movi' fourcc

    public MjpegAviWriter(string path, int width, int height, int fps)
    {
        this.width = width;
        this.height = height;
        this.fps = fps;
        fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        bw = new BinaryWriter(fs);
        WriteHeaders();
    }

    private void FourCC(string s)
    {
        bw.Write((byte)s[0]);
        bw.Write((byte)s[1]);
        bw.Write((byte)s[2]);
        bw.Write((byte)s[3]);
    }

    private void WriteHeaders()
    {
        // RIFF AVI
        FourCC("RIFF");
        riffSizePos = fs.Position;
        bw.Write(0);
        FourCC("AVI ");

        // LIST hdrl
        FourCC("LIST");
        long hdrlSizePos = fs.Position;
        bw.Write(0);
        long hdrlStart = fs.Position;
        FourCC("hdrl");

        // avih (56 bytes)
        FourCC("avih");
        bw.Write(56);
        bw.Write(1000000 / fps);           // microSecPerFrame
        avihMaxBytesPos = fs.Position;
        bw.Write(0);                        // maxBytesPerSec (filled on close)
        bw.Write(0);                        // paddingGranularity
        bw.Write(0x910);                    // flags: AVIF_HASINDEX | AVIF_ISINTERLEAVED | AVIF_TRUSTCKTYPE
        avihTotalFramesPos = fs.Position;
        bw.Write(0);                        // totalFrames (filled on close)
        bw.Write(0);                        // initialFrames
        bw.Write(1);                        // streams
        bw.Write(width * height * 3);       // suggestedBufferSize
        bw.Write(width);
        bw.Write(height);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); // reserved

        // LIST strl
        FourCC("LIST");
        long strlSizePos = fs.Position;
        bw.Write(0);
        long strlStart = fs.Position;
        FourCC("strl");

        // strh (56 bytes)
        FourCC("strh");
        bw.Write(56);
        FourCC("vids");                     // fccType
        FourCC("MJPG");                     // fccHandler
        bw.Write(0);                        // flags
        bw.Write((short)0);                 // priority
        bw.Write((short)0);                 // language
        bw.Write(0);                        // initialFrames
        bw.Write(1);                        // scale
        bw.Write(fps);                      // rate
        bw.Write(0);                        // start
        strhLengthPos = fs.Position;
        bw.Write(0);                        // length (filled on close)
        bw.Write(width * height * 3);       // suggestedBufferSize
        bw.Write(-1);                       // quality
        bw.Write(0);                        // sampleSize
        bw.Write((short)0); bw.Write((short)0); bw.Write((short)width); bw.Write((short)height);

        // strf — BITMAPINFOHEADER (40 bytes)
        FourCC("strf");
        bw.Write(40);
        bw.Write(40);                       // biSize
        bw.Write(width);                    // biWidth
        bw.Write(height);                   // biHeight
        bw.Write((short)1);                 // biPlanes
        bw.Write((short)24);                // biBitCount
        FourCC("MJPG");                     // biCompression
        bw.Write(width * height * 3);       // biSizeImage
        bw.Write(0);                        // biXPelsPerMeter
        bw.Write(0);                        // biYPelsPerMeter
        bw.Write(0);                        // biClrUsed
        bw.Write(0);                        // biClrImportant

        // Fix strl size
        Fixup(strlSizePos, strlStart);
        // Fix hdrl size
        Fixup(hdrlSizePos, hdrlStart);

        // LIST movi
        FourCC("LIST");
        moviListSizePos = fs.Position;
        bw.Write(0);
        long moviTagPos = fs.Position;
        FourCC("movi");
        moviDataStart = moviTagPos; // offset base for idx1 is start of 'movi' tag
    }

    public void AddFrame(byte[] jpeg)
    {
        // offset in idx1 is relative to the start of the 'movi' list data (after the fourcc)
        long chunkPos = fs.Position;
        long offset = chunkPos - moviDataStart;

        FourCC("00dc");
        bw.Write(jpeg.Length);
        bw.Write(jpeg);
        if (jpeg.Length % 2 != 0)
            bw.Write((byte)0); // pad to word boundary

        index.Add((offset, jpeg.Length));
    }

    public void Close()
    {
        int total = index.Count;
        long moviEnd = fs.Position;

        // Fix movi list size
        Fixup(moviListSizePos, moviDataStart);

        // idx1
        FourCC("idx1");
        bw.Write(total * 16);
        foreach (var (offset, size) in index)
        {
            FourCC("00dc");
            bw.Write(0x10);         // AVIIF_KEYFRAME
            bw.Write((int)offset);
            bw.Write(size);
        }

        long fileEnd = fs.Position;

        // Fix RIFF size
        fs.Seek(riffSizePos, SeekOrigin.Begin);
        bw.Write((int)(fileEnd - riffSizePos - 4));

        // Fix totalFrames in avih
        fs.Seek(avihTotalFramesPos, SeekOrigin.Begin);
        bw.Write(total);

        // Fix maxBytesPerSec (rough)
        int avgFrameSize = total > 0 ? (int)((moviEnd - moviDataStart - 4) / total) : 0;
        fs.Seek(avihMaxBytesPos, SeekOrigin.Begin);
        bw.Write(avgFrameSize * fps);

        // Fix strh length
        fs.Seek(strhLengthPos, SeekOrigin.Begin);
        bw.Write(total);

        bw.Flush();
        fs.Close();
    }

    private void Fixup(long sizeFieldPos, long contentStart)
    {
        long end = fs.Position;
        fs.Seek(sizeFieldPos, SeekOrigin.Begin);
        bw.Write((int)(end - contentStart));
        fs.Seek(end, SeekOrigin.Begin);
    }
}
