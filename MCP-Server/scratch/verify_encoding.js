import fs from 'fs';

try {
    const data = fs.readFileSync('rooms_utf8.json', 'utf8');
    const json = JSON.parse(data);

    console.log('--- Room Name Analysis ---');
    if (json[0] && json[0].rooms) {
        json[0].rooms.forEach(room => {
            console.log(`Name: ${room.name}`);
            const codepoints = room.name.split('').map(c => 'U+' + c.charCodeAt(0).toString(16).toUpperCase()).join(' ');
            console.log(`Codepoints: ${codepoints}`);

            // Check for known keywords
            if (room.name.includes('陽台')) console.log('  -> Contains 陽台');
            if (room.name.includes('樓梯')) console.log('  -> Contains 樓梯');
            if (room.name.includes('臥室')) console.log('  -> Contains 臥室');
            if (room.name.includes('客廳')) console.log('  -> Contains 客廳');
            if (room.name.includes('廚房')) console.log('  -> Contains 廚房');
        });
    }
} catch (err) {
    console.error(err);
}
