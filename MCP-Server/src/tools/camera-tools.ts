import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const cameraTools: Tool[] = [
    {
        name: "get_camera_info",
        description: "讀取 3D 視圖／相機的實際位置：眼睛的 XYZ 座標（內部原點座標系 mm）、在哪一層之上、離各樓層多高、視線俯仰角（0° = 水平）。同時讀出性質面板的 Eye Elevation / Target Elevation 參數，並用實際眼睛高度反推該參數的 0 點，逐一比對內部原點、專案基準點、測量點與每個樓層，回報它跟哪一個一致。用途：排查「在 1F 平面放相機，Eye Elevation 卻是負值」這類看不出基準的問題，以及確認人視高是否真的在樓板上方 150–175 cm。唯讀。viewId 可給 3D 視圖本身，也可給平面上選到的相機元件；省略時用目前作用中的 3D 視圖。觸發條件：使用者提到人視高、相機高度、camera、eye elevation、target elevation、視點高度、目標高度、透視圖高度不對、相機在地下。",
        inputSchema: {
            type: "object",
            properties: {
                viewId: {
                    type: "number",
                    description: "3D 視圖或相機元件的 ElementId（省略 = 目前作用中的 3D 視圖）"
                }
            },
            required: []
        }
    }
];
