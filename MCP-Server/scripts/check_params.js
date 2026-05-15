import WebSocket from 'ws';

const ws = new WebSocket('ws://localhost:8964');
ws.on('open', () => {
    ws.once('message', data => {
        console.log(JSON.parse(data.toString()).Data);
        process.exit(0);
    });
    ws.send(JSON.stringify({
        CommandName: 'execute_csharp', 
        Parameters: { 
            Code: `
                var res = new System.Collections.Generic.List<string>();
                var sleeves = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new System.Collections.Generic.List<BuiltInCategory> { 
                        BuiltInCategory.OST_PipeAccessory, 
                        BuiltInCategory.OST_GenericModel 
                    })).ToList();

                foreach(var s in sleeves) {
                    string family = (s as FamilyInstance)?.Symbol?.FamilyName ?? "N/A";
                    string type = s.Name;
                    var p = s.LookupParameter("開孔直徑");
                    var tp = doc.GetElement(s.GetTypeId())?.LookupParameter("開孔直徑");
                    res.Add($"ID:{s.Id}, Family:{family}, Type:{type}, InstP:{(p!=null)}, TypeP:{(tp!=null)}");
                }
                return string.Join("\\n", res);
            ` 
        }, 
        RequestId: 'check-' + Date.now()
    }));
});
