/*******************
Autores:    Angel Garzon Sarzosa (ahgarzon@unicauca.edu.co)
            Jhoan Simei Sarria (simei@unicauca.edu.co)                   
*******************/

using UnityEngine;
using UnityEngine.UI;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Alias para diferenciar entre RosSharp.RosBridgeClient.MessageTypes.Std.String y System.String
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

public class Sendtxt : MonoBehaviour
{
    public RosConnector rosConnector; // Referencia al RosConnector compartido (asignar en el Inspector)
    private RosSocket rosSocket; // Conexión con ROS (compartida)
    private string filePathTopic = "/input_cartesian_path";


    public Button sendHelloButton; // Botón para enviar el mensaje con la ruta del archivo
    public Button resetButton; // Botón para solucionar errores
    public Button loadTxtButton; // Botón para abrir el explorador de archivos

    private string selectedFilePath = "";
    private string initialPath => Application.persistentDataPath;

    void Start()
    {
        // Usar el RosConnector compartido (misma conexión que usan los subscribers) en vez de abrir un socket propio
        if (rosConnector == null)
        {
            rosConnector = FindObjectOfType<RosConnector>();
        }
        if (rosConnector == null)
        {
            UnityEngine.Debug.LogError("Sendtxt: no se encontró ningún RosConnector en la escena.");
            return;
        }

        // Asignar la función al botón de cargar archivo
        if (loadTxtButton != null)
        {
            loadTxtButton.onClick.AddListener(() => StartCoroutine(OpenFileBrowser()));
        }

        // Asignar la función al botón de enviar mensaje
        if (sendHelloButton != null)
        {
            sendHelloButton.onClick.AddListener(() => PublishFilePath());
        }

        StartCoroutine(WaitForConnectionAndAdvertise());
    }

    // Espera a que el RosConnector compartido esté conectado antes de anunciar el tópico
    private IEnumerator WaitForConnectionAndAdvertise()
    {
        while (!rosConnector.IsConnected.WaitOne(0))
        {
            yield return null;
        }

        rosSocket = rosConnector.RosSocket;
        rosSocket.Advertise<RosString>(filePathTopic);
        UnityEngine.Debug.Log("Conectado y tópico anunciado: " + filePathTopic); // Mensaje en español
    }

    // Corrutina para abrir el explorador de archivos (cross-platform)
    private IEnumerator OpenFileBrowser()
    {
        string filePath = "";
        bool done = false;

        yield return new WaitForEndOfFrame();

        System.Threading.Thread t = new System.Threading.Thread(() =>
        {
            try
            {
                filePath = ShowNativeFileDialog(initialPath);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("[Sendtxt] File dialog error: " + e.Message);
            }
            finally
            {
                done = true;
            }
        });

        t.Start();
        while (!done) yield return null;

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            selectedFilePath = filePath;
            UnityEngine.Debug.Log("Archivo .txt seleccionado: " + selectedFilePath);
        }
        else
        {
            UnityEngine.Debug.Log("No se seleccionó ningún archivo.");
            selectedFilePath = "";
        }
    }

    private static string ShowNativeFileDialog(string initialDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ShowFileDialogWindows(initialDir);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return ShowFileDialogLinux(initialDir);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return ShowFileDialogMacOS(initialDir);
        return "";
    }

    private static string ShowFileDialogWindows(string initialDir)
    {
        string safeDir = initialDir.Replace("\\", "\\\\").Replace("'", "\\'");
        string psScript =
            "[System.Windows.Forms.Application]::EnableVisualStyles();" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog;" +
            $"$d.InitialDirectory = '{safeDir}';" +
            "$d.Filter = 'Text files (*.txt)|*.txt|All files (*.*)|*.*';" +
            "$d.Title = 'Selecciona un archivo .txt';" +
            "if ($d.ShowDialog() -eq 'OK') { $d.FileName }";
        TryRunProcess("powershell", $"-NoProfile -NonInteractive -Command \"{psScript}\"", out string result);
        return result.Trim();
    }

    private static string ShowFileDialogLinux(string initialDir)
    {
        if (TryRunProcess("zenity",   $"--file-selection --title=\"Selecciona un archivo .txt\" --filename=\"{initialDir}/\" --file-filter=\"*.txt\"", out string z)) return z.Trim();
        if (TryRunProcess("kdialog", $"--getopenfilename \"{initialDir}\" \"Text files (*.txt)\"", out string k)) return k.Trim();
        UnityEngine.Debug.LogWarning("[Sendtxt] No file dialog tool found (install zenity or kdialog).");
        return "";
    }

    private static string ShowFileDialogMacOS(string initialDir)
    {
        string escaped = initialDir.Replace("\"", "\\\"");
        string script = $"POSIX path of (choose file with prompt \"Selecciona un archivo .txt\" default location POSIX file \"{escaped}\")";
        TryRunProcess("osascript", $"-e '{script}'", out string result);
        return result.Trim();
    }

    private static bool TryRunProcess(string exe, string args, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch { return false; }
    }

    // Método que publica la ruta del archivo en el tópico
    private void PublishFilePath()
    {
        if (rosSocket == null)
        {
            UnityEngine.Debug.LogWarning("Sendtxt: RosSocket aún no está conectado.");
            return;
        }
        if (!string.IsNullOrEmpty(selectedFilePath))
        {
            // Crear el mensaje con la ruta del archivo
            RosString message = new RosString { data = selectedFilePath };
            rosSocket.Publish(filePathTopic, message);
            UnityEngine.Debug.Log("Ruta del archivo publicada en el tópico: " + filePathTopic); // Mensaje en español
        }
        else
        {
            UnityEngine.Debug.Log("No hay archivo seleccionado. Por favor, selecciona un archivo .txt antes de enviar."); // Mensaje en español
        }
    }

}