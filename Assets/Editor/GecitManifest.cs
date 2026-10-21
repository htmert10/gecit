using System.IO;
using UnityEditor.Android;

// Kumanda için Bluetooth izinlerini Android manifestine ekler (Unity ayarlarında bu izinler yok).
public class GecitManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    static readonly (string name, string line)[] Permissions =
    {
        ("android.permission.BLUETOOTH_CONNECT", "<uses-permission android:name=\"android.permission.BLUETOOTH_CONNECT\" />"),
        ("android.permission.BLUETOOTH", "<uses-permission android:name=\"android.permission.BLUETOOTH\" android:maxSdkVersion=\"30\" />"),
        ("android.permission.BLUETOOTH_ADMIN", "<uses-permission android:name=\"android.permission.BLUETOOTH_ADMIN\" android:maxSdkVersion=\"30\" />"),
    };

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifest)) return;
        var text = File.ReadAllText(manifest);
        foreach (var (name, line) in Permissions)
        {
            if (text.Contains("\"" + name + "\"")) continue;
            int at = text.IndexOf("<application");
            if (at < 0) return;
            text = text.Insert(at, line + "\n  ");
        }
        File.WriteAllText(manifest, text);
        UnityEngine.Debug.Log("GECIT MANIFEST bluetooth izinleri eklendi");
    }
}
