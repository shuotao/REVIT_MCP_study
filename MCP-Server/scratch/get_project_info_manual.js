import { RevitSocketClient } from "../build/socket.js";
import fs from 'fs';
import path from 'path';

const client = new RevitSocketClient('localhost', 11111);

async function main() {
    try {
        console.error("Connecting to Revit at port 11111...");
        await client.connect();
        console.error("Connected. Sending get_project_info command...");

        const response = await client.sendCommand('get_project_info', {});

        if (response.success) {
            const outputPath = path.join(process.cwd(), 'scratch', 'project_info.json');
            fs.writeFileSync(outputPath, JSON.stringify(response.data, null, 2));
            console.error(`Success! Info saved to ${outputPath}`);
        } else {
            console.error("Command failed:", response.error);
        }
    } catch (err) {
        console.error("Execution failed:", err);
    } finally {
        client.disconnect();
        process.exit(0);
    }
}

main();
