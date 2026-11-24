using System.Collections.Generic;
using System.Linq;
using EasyLocalization;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkTypographyStudio
    {
        private static readonly (string id,string en,string ru)[] Strings={
            ("open","Text","Текст"),("heading","Typography","Типографика"),("speaker","Ferryman","Паромщик"),
            ("body","The bridge is open. Bring the parcel to the far bank.","Мост открыт. Доставьте посылку на другой берег."),
            ("caption","Delivery №7  ·  25%  ·  5 ₽  ·  →","Заказ №7  ·  Ёж  ·  25%  ·  5 ₽  ·  →"),
            ("marker","●  Ferry crossing","●  Переправа"),("size","Text size","Размер"),("motion","Motion","Движение"),
            ("contrast","Contrast","Контраст"),("off","Off","Выкл."),("on","On","Вкл."),("high","High","Высокий"),("normal","Normal","Обычный")
        };
        internal static void Apply(EasySuiteLab lab)
        {
            var manager=lab.Windows;
            if(!manager.Typography)
            {
                string path=EasySuiteSceneBuilder.Content+"/UI/Playground Typography.asset";
                var theme=AssetDatabase.LoadAssetAtPath<UITypography>(path);
                if(!theme){theme=Object.Instantiate(UITypography.Default);theme.name="Playground Typography";AssetDatabase.CreateAsset(theme,path);}
                manager.Typography=theme;EditorUtility.SetDirty(manager);
            }
            var sample=lab.GetComponent<EasySuiteTypography>();
            if(!sample)sample=lab.gameObject.AddComponent<EasySuiteTypography>();sample.lab=lab;EditorUtility.SetDirty(sample);
            void Role(TMP_Text text,UITextRole role)
            {
                if(!(text is TextMeshProUGUI ui) || ui.GetComponent<UIStyledText>())return;
                UIText.Style(ui,role);EditorUtility.SetDirty(ui);
            }
            var journal=manager.GetComponentInChildren<EasyQuests.UI.QuestJournal>(true);
            if(journal){
                Role(journal.body,UITextRole.Body);
                foreach(var btn in journal.GetComponentsInChildren<UnityEngine.UI.Button>(true)) Role(btn.GetComponentInChildren<TextMeshProUGUI>(true),UITextRole.Button);
            }
            Role(lab.Notice,UITextRole.Caption);
            InstallText(lab, Strings);
        }
        internal static void AddRevealTranslations(EasySuiteLab lab) => InstallText(lab,
            new[]{("replay","Replay subtitle","Повторить субтитр")});
        private static void InstallText(EasySuiteLab lab,(string id,string en,string ru)[] strings)
        {
            var collection=LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var entries=new List<CatalogTranslation>(lab.Texts.catalog.entries);
            foreach(var row in strings)
            {
                string key="type."+row.id;
                foreach(var lang in new[]{"en","ru"}){
                    var t=(StringTable)collection.GetTable(new LocaleIdentifier(lang));
                    if(t.GetEntry(key)==null){t.AddEntry(key,lang=="en"?row.en:row.ru);EditorUtility.SetDirty(t);}
                }
                if(entries.All(e=>e.id!=key))entries.Add(new CatalogTranslation{id=key,text=new LocalizedString(collection.SharedData.TableCollectionNameGuid,collection.SharedData.GetEntry(key).Id)});
            }
            lab.Texts.catalog.SetEntries(entries.ToArray());EditorUtility.SetDirty(lab.Texts.catalog);
            EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssets();
        }
    }
}
