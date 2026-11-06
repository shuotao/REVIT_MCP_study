using System.Text.RegularExpressions;
namespace RevitMCP.Core {
    public static class BeamNumberRules {
        public static string TypeName(string label) {
            if(label==null)return null;
            label=label.Trim();
            if(label=="Fb0")return "50x270";
            if(Regex.IsMatch(label,@"\AFWB[1-5]\z"))return "60x270";
            if(Regex.IsMatch(label,@"\A(?:FB|FG)[0-9]+\z"))return "70x270";
            if(Regex.IsMatch(label,@"\A(?:Fb|Fg)[0-9]+\z"))return "60x270";
            return null;
        }
        public static double WidthMm(string typeName) =>
            typeName=="50x270" ? 500 : typeName=="60x270" ? 600 : typeName=="70x270" ? 700 : 0;
    }
}
