using System;
using System.Collections.Generic;
using UnityEditor.Localization;
using UnityEngine;

namespace EasyNarrative.Editor
{
    [Serializable, StringTableCollectionExtension]
    public sealed class NarrativeSourceHistory : CollectionExtension
    {
        [Serializable]
        public sealed class Review
        {
            public string locale, previousTranslation, previousSource;
        }

        [Serializable]
        public sealed class Location
        {
            public string owner, context, text;
        }

        [Serializable]
        public sealed class Record
        {
            public long entryId;
            public string locale, graphText, primaryText;
            public List<Review> reviews = new();
            public List<Location> locations = new();
        }

        [HideInInspector] public List<Record> records = new();
    }
}
