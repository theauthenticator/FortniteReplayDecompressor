const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const CUE4PARSE = 'C:\\Users\\enoch\\Downloads\\CUE4Parse.CLI-0.0.8-Win64-bin\\cue4parse.exe';
const GAME_DIR = 'C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Content\\Paks';
const OUTPUT_DIR = 'C:\\Users\\enoch\\Downloads\\FModel\\Output\\Exports\\Parents';

const patterns = [
    'FortniteGame/Content/Environments/Asteria/Foliage/Trees/Blueprint_Parent/*Parent*.uasset'
];

async function exportPattern(pattern) {
    return new Promise((resolve, reject) => {
        console.log(`\nExporting: ${pattern}\n`);
        console.log('='.repeat(80));
        
        const args = [
            '-i', GAME_DIR,
            '-o', OUTPUT_DIR,
            '-p', pattern,
            '-g', 'GAME_UE5_LATEST',
            '-f', 'json',
            '-y',
            '-v'  // Verbose!
        ];
        
        console.log(`Command: ${CUE4PARSE} ${args.join(' ')}\n`);
        
        const proc = spawn(CUE4PARSE, args);
        
        proc.stdout.on('data', (data) => {
            process.stdout.write(data);
        });
        
        proc.stderr.on('data', (data) => {
            process.stderr.write(data);
        });
        
        proc.on('close', (code) => {
            console.log(`\n${'='.repeat(80)}`);
            console.log(`Exit code: ${code}\n`);
            resolve(code);
        });
        
        proc.on('error', (err) => {
            console.error(`Process error: ${err.message}`);
            reject(err);
        });
    });
}

async function main() {
    console.log('Testing CUE4Parse with full error output...\n');
    
    if (!fs.existsSync(OUTPUT_DIR)) {
        fs.mkdirSync(OUTPUT_DIR, { recursive: true });
    }
    
    // Test with just ONE pattern to see full error
    const code = await exportPattern(patterns[0]);
    
    if (code === 0) {
        console.log('✅ SUCCESS! Export worked!');
    } else {
        console.log(`❌ FAILED with exit code: ${code}`);
    }
    
    // Check if any files were created
    try {
        const files = fs.readdirSync(OUTPUT_DIR);
        console.log(`\nFiles in output directory: ${files.length}`);
        files.forEach(f => console.log(`  - ${f}`));
    } catch (err) {
        console.log('\nNo files in output directory');
    }
}

main().catch(console.error);