using System.IO;
using System.Xml.Serialization;
using abldeployer.Core.Config;

namespace csdeployer.Core {


    public class CsDeployerConfig {
        internal static CsConfigDeploymentPackaging Load(string path) {
            CsDeployerConfigXml configXml;
            var serializer = new XmlSerializer(typeof(CsDeployerConfigXml));
            using (var reader = new StreamReader(path)) {
                configXml = (CsDeployerConfigXml)serializer.Deserialize(reader);
            }
            return GetConfigDeploymentPackaging(configXml);
        }

        internal static void Save(CsConfigDeploymentPackaging config, string path) {
            CsDeployerConfigXml configXml = GetConfigXml(config);
            var serializer = new XmlSerializer(typeof(CsDeployerConfigXml));
            using (TextWriter writer = new StreamWriter(path, false)) {
                serializer.Serialize(writer, configXml);
            }
        }

        private static CsConfigDeploymentPackaging GetConfigDeploymentPackaging(CsDeployerConfigXml configXml) {
            var config = new CsConfigDeploymentPackaging {
                
            };
            return config;
        }

        private static CsDeployerConfigXml GetConfigXml(CsConfigDeploymentPackaging config) {
            return null;
        }

       
        
    }
}
