using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Editor
{
    public static class TranslationComments
    {
        public static string Read(SharedTableData.SharedTableEntry entry) =>
            entry?.Metadata.GetMetadata<Comment>()?.CommentText ?? "";

        public static void Write(SharedTableData.SharedTableEntry entry, string text)
        {
            var comment = entry.Metadata.GetMetadata<Comment>();
            if (comment == null)
            {
                if (string.IsNullOrEmpty(text)) return;
                comment = new Comment();
                entry.Metadata.AddMetadata(comment);
            }
            comment.CommentText = text ?? "";
        }
    }
}
