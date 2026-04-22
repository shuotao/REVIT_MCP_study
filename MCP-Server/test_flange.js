import WebSocket from 'ws';
const ws = new WebSocket('ws://localhost:11111');
ws.on('open', () => {
    const pythonScript = `
import Autodesk.Revit.DB as DB
doc = __revit__.ActiveUIDocument.Document
# 找到畫面中任何一個法蘭
coll = DB.FilteredElementCollector(doc).OfCategory(DB.BuiltInCategory.OST_PipeFitting).WhereElementIsNotElementType().ToElements()
flange = None
for e in coll:
    if 'DN15 - DN300' in e.Name:
        flange = e
        break

if flange:
    out = []
    p = flange.LookupParameter('DN1')
    if p:
        out.append("DN1 on Instance: val={}, RO={}".format(p.AsDouble(), p.IsReadOnly))
    else:
        out.append("DN1 not found on Instance")
        
    p_type = flange.Symbol.LookupParameter('DN1')
    if p_type:
        out.append("DN1 on Type: val={}, RO={}".format(p_type.AsDouble(), p_type.IsReadOnly))
        
    for p_name in ["公稱直徑", "Nominal Diameter", "Size"]:
        pp = flange.LookupParameter(p_name)
        if pp:
            out.append(p_name + ": RO=" + str(pp.IsReadOnly))
            
    print("; ".join(out))
else:
    print("Flange not found")
`;
    ws.send(JSON.stringify({ CommandName: 'run_python', Parameters: { script: pythonScript }, RequestId: 'rt' }));
});
ws.on('message', (data) => {
    console.log(JSON.parse(data.toString()));
    ws.close();
    process.exit(0);
});
ws.on('error', (e) => { console.error(e.message); process.exit(1); });
setTimeout(() => { process.exit(1); }, 4000);
