using System.IO;
using System.Xml.Serialization;
using abldeployer.Core;

namespace csdeployer.Core {

    class ConfigXml : Config.ProConfig {

        #region Fields

        private static Config.ProConfig _currentEnv;

        #endregion

        #region Public

        /// <summary>
        ///     Return the current ProgressEnvironnement object (null if the list is empty!)
        /// </summary>
        public static Config.ProConfig Instance {
            get {
                if (_currentEnv == null) _currentEnv = new Config.ProConfig();
                return _currentEnv;
            }
            set { _currentEnv = value; }
        }

        public static Config.ProConfig Load(string path) {
            var serializer = new XmlSerializer(typeof(Config.ProConfig));
            using (var reader = new StreamReader(path)) {
                return (Config.ProConfig)serializer.Deserialize(reader);
            }
        }

        public static void Save(Config.ProConfig config, string path) {
            var serializer = new XmlSerializer(typeof(Config.ProConfig));
            using (TextWriter writer = new StreamWriter(path, false)) {
                serializer.Serialize(writer, config);
            }
        }

        #endregion


    }
}
