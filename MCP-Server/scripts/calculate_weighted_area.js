import fs from 'fs';

try {
    const data = fs.readFileSync('rooms_utf8.json', 'utf8');
    const levels = JSON.parse(data);

    console.log('--- Weighted Area Calculation Report ---');
    console.log('Rules: Main=100%, Balcony(陽台)=50%, Stair(樓梯/梯間)=0%');
    console.log('-------------------------------------------------------------------------');
    console.log('| Level | No. | Name                | Area (m²) | Weight | W. Area (m²) |');
    console.log('-------------------------------------------------------------------------');

    let totalWeightedArea = 0;

    levels.forEach(level => {
        if (level.rooms.length === 0) return;

        level.rooms.forEach(room => {
            let weight = 1.0;
            let type = 'Main';

            if (room.name.includes('陽台')) {
                weight = 0.5;
                type = 'Balcony';
            } else if (room.name.includes('樓梯') || room.name.includes('梯間')) {
                weight = 0.0;
                type = 'Stair';
            }

            const weightedArea = room.area * weight;
            totalWeightedArea += weightedArea;

            console.log(
                `| ${level.name.padEnd(6)} ` +
                `| ${room.number.padEnd(4)} ` +
                `| ${room.name.padEnd(8)} ` +
                `| ${room.area.toFixed(2).padStart(8)} ` +
                `| ${weight.toFixed(1).padStart(4)} ` +
                `| ${weightedArea.toFixed(2).padStart(10)} |`
            );
        });
        console.log('---------------------------------------------------------');
    });

    console.log(`\nTotal Weighted Area: ${totalWeightedArea.toFixed(2)} m²`);

} catch (err) {
    console.error('Error:', err);
}
