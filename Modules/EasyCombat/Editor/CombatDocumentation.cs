using System.IO;
using UnityEditor;

namespace EasyCombat.Editor
{
    public static class CombatDocumentation
    {
        [MenuItem("Tools/EasyGameFramework/Combat/Documentation/Open Local Guide")]
        public static void Open()
        {
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Health).Assembly);
            var root=package!=null ? package.resolvedPath : Path.GetFullPath("Packages/com.oreethehat.easy-game-framework");
            EditorUtility.RevealInFinder(Path.Combine(root,"Modules","EasyCombat","README.md"));
        }
    }
}
