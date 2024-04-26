using System;
using System.ComponentModel;
using System.IO;
using lib.remnant2.analyzer;

namespace RemnantSaveGuardian
{
    public class SaveBackup : IEditableObject
    {
        struct SaveData
        {
            internal string name;
            internal DateTime date;
            internal bool keep;
            internal bool active;
        }

        public event EventHandler<UpdatedEventArgs> Updated;
        private SaveData saveData;
        private SaveData backupData;
        private bool inTxn = false;
        //private int[] progression;
        //private List<RemnantCharacter> charData;
        private string progression;
        private string savePath;

        public string Name
        {
            get
            {
                return saveData.name;
            }
            set
            {
                if (value.Equals(""))
                {
                    saveData.name = saveData.date.Ticks.ToString();
                }
                else
                {
                    saveData.name = value;
                }
                //OnUpdated(new UpdatedEventArgs("Name"));
            }
        }
        public DateTime SaveDate
        {
            get
            {
                return saveData.date;
            }
            set
            {
                saveData.date = value;
                //OnUpdated(new UpdatedEventArgs("SaveDate"));
            }
        }
        public string Progression
        {
            get
            {
                return string.Join(", ", progression);
            }
        }
        public bool Keep
        {
            get
            {
                return saveData.keep;
            }
            set
            {
                if (saveData.keep != value)
                {
                    saveData.keep = value;
                    OnUpdated(new UpdatedEventArgs("Keep"));
                }
            }
        }
        public bool Active
        {
            get
            {
                return saveData.active;
            }
            set
            {
                saveData.active = value;
                //OnUpdated(new UpdatedEventArgs("Active"));
            }
        }

        public string SaveFolderPath
        {
            get
            {
                return savePath;
            }
        }

        //public SaveBackup(DateTime saveDate)
        public SaveBackup(string savePath)
        {
            this.savePath = savePath;

            progression = Analyzer.GetProfileStringCombined(this.savePath);
            saveData = new SaveData();
            saveData.name = SaveDateTime.Ticks.ToString();
            saveData.date = SaveDateTime;
            saveData.keep = false;
        }

        /*public void setProgression(List<List<string>> allItemList)
        {

            int[] prog = new int[allItemList.Count];
            for (int i=0; i < allItemList.Count; i++)
            {
                prog[i] = allItemList[i].Count;
            }
            this.progression = prog;
        }
        public List<RemnantCharacter> GetCharacters()
        {
            return this.charData;
        }
        public void LoadCharacterData(string saveFolder)
        {
            this.charData = RemnantCharacter.GetCharactersFromSave(saveFolder, RemnantCharacter.CharacterProcessingMode.NoEvents);
        }*/

        // Implements IEditableObject
        void IEditableObject.BeginEdit()
        {
            if (!inTxn)
            {
                backupData = saveData;
                inTxn = true;
            }
        }

        void IEditableObject.CancelEdit()
        {
            if (inTxn)
            {
                saveData = backupData;
                inTxn = false;
            }
        }

        void IEditableObject.EndEdit()
        {
            if (inTxn)
            {
                if (!backupData.name.Equals(saveData.name))
                {
                    OnUpdated(new UpdatedEventArgs("Name"));
                }
                if (!backupData.date.Equals(saveData.date))
                {
                    OnUpdated(new UpdatedEventArgs("SaveDate"));
                }
                if (!backupData.keep.Equals(saveData.keep))
                {
                    OnUpdated(new UpdatedEventArgs("Keep"));
                }
                if (!backupData.active.Equals(saveData.active))
                {
                    OnUpdated(new UpdatedEventArgs("Active"));
                }
                backupData = new SaveData();
                inTxn = false;
            }
        }

        public void OnUpdated(UpdatedEventArgs args)
        {
            EventHandler<UpdatedEventArgs> handler = Updated;
            if (null != handler) handler(this, args);
        }

        private DateTime SaveDateTime
        {
            get
            {
                return File.GetLastWriteTime(Path.Join(savePath, "profile.sav"));
            }
        }
    }

    public class UpdatedEventArgs : EventArgs
    {
        private readonly string _fieldName;

        public UpdatedEventArgs(string fieldName)
        {
            _fieldName = fieldName;
        }

        public string FieldName
        {
            get { return _fieldName; }
        }
    }
}
