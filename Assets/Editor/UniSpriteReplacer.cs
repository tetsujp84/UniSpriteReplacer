using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// セットされているスプライトを置換するエディタ拡張
/// </summary>
public class UniSpriteReplacer : EditorWindow
{
    private const string DefaultFileId = "21300000";
    // 検索対象の拡張子をここに登録する
    private static string[] Extensions = { ".prefab", ".unity" };

    private static List<string> replacePathList = new List<string>();
    private static Sprite spriteTargetBefore;
    private static Sprite spriteTargetAfter;

    /// <summary>
    /// メニューを表示する
    /// </summary>
    [MenuItem("Window/SpriteReplacer")]
    static void Open()
    {
        EditorWindow.GetWindow<UniSpriteReplacer>("SpriteReplacer");
    }

    /// <summary>
    /// メニューの表示
    /// </summary>
    void OnGUI()
    {
        // 置換前
        GUILayout.BeginHorizontal();
        GUILayout.Label("置換前", GUILayout.Width(76f));
        spriteTargetBefore = EditorGUILayout.ObjectField(spriteTargetBefore, typeof(Sprite), false) as Sprite;
        GUILayout.EndHorizontal();

        // 置換後
        GUILayout.BeginHorizontal();
        GUILayout.Label("置換後", GUILayout.Width(76f));
        spriteTargetAfter = EditorGUILayout.ObjectField(spriteTargetAfter, typeof(Sprite), false) as Sprite;
        GUILayout.EndHorizontal();

        EditorGUILayout.BeginVertical();
        if(GUILayout.Button("ReplaceAll", GUILayout.Width(100))) {
            tryReplaceSprite();
        } else if(GUILayout.Button("ReplaceInScene", GUILayout.Width(100))) {
            tryReplaceSprite(isScene: true);
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 一連の置換処理を開始する
    /// </summary>
    /// <param name="isScene">シーン内のみ適用か</param>
    /// <remarks>シーン内適用ではシーン内のプレハブには適用しないので注意</remarks>
    private static void tryReplaceSprite(bool isScene = false)
    {
        if(spriteTargetBefore == null || spriteTargetAfter == null) {
            UnityEngine.Debug.Log("対象がセットされていません");
            return;
        }

        var idAndGuidStr = new string[2];
        // 書き換え元のファイルのGUIDとFILEIDを取得
        idAndGuidStr[0] = GetFileIdAndGuid(spriteTargetBefore);
        // 書き換え後のファイルのGUIDとFILEIDを取得
        idAndGuidStr[1] = GetFileIdAndGuid(spriteTargetAfter);

        if(idAndGuidStr[0] == null || idAndGuidStr[1] == null) {
            return;
        }
        // すべてのアセットのパスを取得
        var allPaths = AssetDatabase.GetAllAssetPaths();

        // シーンファイルのみ
        if(isScene) {
            Debug.Log("シーン内のみに置換実行");
            allPaths = allPaths.Where(path => path.Contains(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)).ToArray();
        }

        replacePathList = new List<string>();
        var length = allPaths.Length;
        for(var i = 0; i < length; i++) {
            // プログレスバー
            EditorUtility.DisplayProgressBar("置換中...", string.Format("{0}/{1}", i + 1, length), (float)i / length);
            // 置換対象のプロパティを検索
            if(Extensions.Contains(Path.GetExtension(allPaths[i]))) {
                replaceFile(allPaths[i], idAndGuidStr);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();

        // 結果出力
        var result = EditorWindow.GetWindow<SpriteReplacerResult>("Result");
        var spriteNames = new string[2];
        spriteNames[0] = spriteTargetBefore.name;
        spriteNames[1] = spriteTargetAfter.name;
        result.SetData(replacePathList, idAndGuidStr, spriteNames);
    }

    /// <summary>
    /// ファイルの書き換え
    /// </summary>
    /// <param name="path">置換対象のシーンまたはプレハブのファイルパス</param>
    /// <param name="targets">置換前後のfileIDとGUID</param>
    private static void replaceFile(string path, string[] targets)
    {
        var input = File.ReadAllText(path);
        // 対象ファイルから文字列検索
        if(Regex.IsMatch(input, targets[0])) {
            // 置換実行
            input = Regex.Replace(input, targets[0], targets[1]);
            File.WriteAllText(path, input);
            // 置換済みリストに追加
            replacePathList.Add(path);
        }
    }

    /// <summary>
    /// ターゲットスプライトからfileID+GUIDを読み込む
    /// </summary>
    /// <returns>"fileID: " ID ",guid" guid 形式で文字列を返す </returns>
    /// <param name="target">取得元Sprite</param>
    private static string GetFileIdAndGuid(Sprite target)
    {
        // 書き換え元のファイルGUIDとfileIDを取得
        var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target.texture));
        var path = AssetDatabase.GetAssetPath(target.texture);
        var textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;

        // fileIDのセット
        var fileId = DefaultFileId;

        // 他のアセットの一部ならfileID変更
        if(textureImporter.spritesheet.Length != 0) {
            path += ".meta";
            var matchStr = @"(?<fileID>[0-9]*): " + target.name + "\n";
            var match = new Regex(matchStr).Match(File.ReadAllText(path));
            if(match.Success) {
                fileId = match.Groups["fileID"].Value;
            } else {
                Debug.Log("fileID取得失敗");
                return null;
            }
        } else {
            Debug.Log("fileIDを初期値にします");
        }
        return "fileID: " + fileId + ", guid: " + guid;
    }
}

/// <summary>
/// 結果表示用UI
/// </summary>
public class SpriteReplacerResult : EditorWindow
{
    private const float FieldWidth = 100;
    private Vector2 scrollPos;
    private List<string> replacePathList = new List<string>();
    private string[] idAndGuid = new string[2];
    private string[] fileNames = new string[2];

    /// <summary>
    /// 表示内容のセット
    /// </summary>
    /// <param name="list">変更したファイルのパス</param>
    /// <param name="idAndGuid">変更前後のfileIDとguid</param>
    /// <param name="names">ファイル名</param>
    public void SetData(List<string> list, string[] idAndGuid, string[] names)
    {
        replacePathList = list;
        this.idAndGuid = idAndGuid;
        this.fileNames = names;
    }

    /// <summary>
    /// UI表示
    /// </summary>
    void OnGUI()
    {
        string[] replaceText = { "置換前", "置換後" };

        // 置換前情報
        for(var i = 0; i < 2; i++) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(replaceText[i], GUILayout.Width(FieldWidth));
            EditorGUILayout.LabelField("fileID&GUID", GUILayout.Width(FieldWidth));
            EditorGUILayout.TextField(idAndGuid[i]);
            EditorGUILayout.LabelField("ファイル名", GUILayout.Width(FieldWidth));
            EditorGUILayout.TextField(fileNames[i]);
            EditorGUILayout.EndHorizontal();
        }

        // リスト表示
        EditorGUILayout.LabelField("置換ファイル", GUILayout.Width(FieldWidth));
        if(replacePathList.Count != 0) {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach(var path in replacePathList) {
                EditorGUILayout.TextField(path);
            }
            EditorGUILayout.EndScrollView();
        } else {
            EditorGUILayout.LabelField("置換対象がありませんでした");
        }
    }
}


