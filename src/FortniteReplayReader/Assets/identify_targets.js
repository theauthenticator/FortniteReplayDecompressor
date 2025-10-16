const fs = require('fs');
const path = require('path');
const readline = require('readline');

async function extractParentPathsLineByLine(assetRegistryPath, outputPath) {
    return new Promise((resolve, reject) => {
        console.log('🔍 Extracting parent paths (line-by-line)...\n');
        
        const readStream = fs.createReadStream(assetRegistryPath, {
            encoding: 'utf8',
            highWaterMark: 64 * 1024 // 64KB chunks
        });
        
        const rl = readline.createInterface({
            input: readStream,
            crlfDelay: Infinity
        });
        
        const writeStream = fs.createWriteStream(outputPath, { encoding: 'utf8' });
        const seenPaths = new Set();
        
        let lineCount = 0;
        let parentCount = 0;
        let assetsWithParents = 0;
        
        rl.on('line', (line) => {
            lineCount++;
            
            if (lineCount % 100000 === 0) {
                process.stdout.write(`\r   Lines: ${lineCount.toLocaleString()} | Parents: ${parentCount.toLocaleString()}  `);
                
                // Clear Set periodically to save memory
                if (seenPaths.size > 30000) {
                    seenPaths.clear();
                }
            }
            
            // Look for ParentClass in the line
            if (line.includes('"ParentClass"')) {
                // Extract the value after "ParentClass": "
                const match = line.match(/"ParentClass":\s*"([^"]+)"/);
                if (match) {
                    const parentClass = match[1];
                    assetsWithParents++;
                    
                    // Extract path from strings like:
                    // "BlueprintGeneratedClass'/Game/Path/To/Parent.Parent_C'"
                    const pathMatch = parentClass.match(/'([^']+)'/);
                    if (pathMatch) {
                        const fullPath = pathMatch[1];
                        
                        // Only Blueprint paths (starting with /)
                        if (fullPath.startsWith('/')) {
                            const parentPath = fullPath.split('.')[0]; // Remove .Parent_C
                            
                            if (!seenPaths.has(parentPath)) {
                                seenPaths.add(parentPath);
                                writeStream.write(parentPath + '\n');
                                parentCount++;
                            }
                        }
                    }
                }
            }
        });
        
        rl.on('close', () => {
            writeStream.end(() => {
                console.log(`\n\n✅ Processed ${lineCount.toLocaleString()} lines`);
                console.log(`✅ Found ${parentCount.toLocaleString()} unique parent paths`);
                console.log(`✅ ${assetsWithParents.toLocaleString()} assets have parents\n`);
                resolve({ parentCount, assetsWithParents });
            });
        });
        
        rl.on('error', reject);
        readStream.on('error', reject);
    });
}

function convertUnrealPathToFilePath(unrealPath) {
    if (unrealPath.startsWith('/Game/')) {
        return 'FortniteGame/Content/' + unrealPath.substring(6) + '.json';
    } else if (unrealPath.startsWith('/')) {
        return unrealPath.substring(1) + '.json';
    }
    return unrealPath + '.json';
}

async function analyzeAndGroupPaths(parentPathsFile, outputDir) {
    console.log('📊 Analyzing parent paths...\n');
    
    return new Promise((resolve, reject) => {
        const folderCounts = new Map();
        
        const rl = readline.createInterface({
            input: fs.createReadStream(parentPathsFile),
            crlfDelay: Infinity
        });
        
        let lineCount = 0;
        
        rl.on('line', (unrealPath) => {
            lineCount++;
            
            const filePath = convertUnrealPathToFilePath(unrealPath);
            const folder = path.dirname(filePath);
            const parts = folder.split(path.sep);
            const topLevel = parts.slice(0, Math.min(3, parts.length)).join(path.sep);
            
            folderCounts.set(topLevel, (folderCounts.get(topLevel) || 0) + 1);
        });
        
        rl.on('close', () => {
            console.log(`✅ Analyzed ${lineCount.toLocaleString()} parent paths\n`);
            
            const sortedFolders = Array.from(folderCounts.entries())
                .sort((a, b) => b[1] - a[1]);
            
            console.log('📊 Top 30 folders:\n');
            sortedFolders.slice(0, 30).forEach(([folder, count], i) => {
                console.log(`   ${(i + 1).toString().padStart(2)}. ${count.toString().padStart(6)} files - ${folder}`);
            });
            
            // Save summary
            const summaryPath = path.join(outputDir, 'FolderSummary.txt');
            const lines = [
                '='.repeat(80),
                'FMODEL EXPORT GUIDE',
                '='.repeat(80),
                '',
                `Total parent blueprints: ${lineCount.toLocaleString()}`,
                '',
                '='.repeat(80),
                'TOP 50 FOLDERS TO EXPORT (in priority order):',
                '='.repeat(80),
                ''
            ];
            
            sortedFolders.slice(0, 50).forEach(([folder, count], i) => {
                lines.push(`${(i + 1).toString().padStart(3)}. ${folder}`);
                lines.push(`     ${count.toLocaleString()} parent blueprints`);
                lines.push('');
            });
            
            lines.push('');
            lines.push('='.repeat(80));
            lines.push('HOW TO EXPORT IN FMODEL:');
            lines.push('='.repeat(80));
            lines.push('1. Open FModel');
            lines.push('2. Navigate to a folder from the list above');
            lines.push('   Example: FortniteGame → Content → Environments');
            lines.push('3. Right-click the folder');
            lines.push('4. Select "Save Folder\'s Packages Properties (.json)"');
            lines.push('5. Wait for export to complete');
            lines.push('6. Repeat for other top folders');
            lines.push('');
            lines.push('TIP: Export the top 5-10 folders for best coverage!');
            lines.push('');
            
            fs.writeFileSync(summaryPath, lines.join('\n'), 'utf8');
            console.log(`\n✅ Saved: ${summaryPath}`);
            
            resolve({ sortedFolders, totalPaths: lineCount });
        });
        
        rl.on('error', reject);
    });
}

async function convertToFilePaths(parentPathsFile, outputFile) {
    console.log('\n📝 Converting to file system paths...\n');
    
    return new Promise((resolve, reject) => {
        const rl = readline.createInterface({
            input: fs.createReadStream(parentPathsFile),
            crlfDelay: Infinity
        });
        
        const writeStream = fs.createWriteStream(outputFile, { encoding: 'utf8' });
        let count = 0;
        
        rl.on('line', (unrealPath) => {
            const filePath = convertUnrealPathToFilePath(unrealPath);
            writeStream.write(filePath + '\n');
            count++;
            
            if (count % 5000 === 0) {
                process.stdout.write(`\r   Converted: ${count.toLocaleString()} paths...`);
            }
        });
        
        rl.on('close', () => {
            writeStream.end(() => {
                console.log(`\n✅ Saved: ${outputFile}`);
                console.log(`   Total: ${count.toLocaleString()} file paths\n`);
                resolve(count);
            });
        });
        
        rl.on('error', reject);
    });
}

async function main() {
    console.log('🎮 Fortnite Parent Blueprint Extractor');
    console.log('='.repeat(80));
    console.log('Ultra-lightweight line-by-line parser\n');

    const assetRegistryPath = 'C:\\Users\\enoch\\Downloads\\FModel\\Output\\Exports\\FortniteGame\\AssetRegistry.json';
    const outputDir = 'C:\\Users\\enoch\\Downloads';
    const tempParentPaths = path.join(outputDir, 'temp_parent_paths.txt');
    const exportListPath = path.join(outputDir, 'ExportList_AllParents.txt');

    try {
        console.log('Starting extraction... This may take 2-3 minutes.\n');
        
        // Step 1: Extract parent paths
        await extractParentPathsLineByLine(assetRegistryPath, tempParentPaths);
        
        // Step 2: Analyze and group
        await analyzeAndGroupPaths(tempParentPaths, outputDir);
        
        // Step 3: Convert to file paths
        await convertToFilePaths(tempParentPaths, exportListPath);
        
        // Clean up temp file
        fs.unlinkSync(tempParentPaths);
        
        console.log('='.repeat(80));
        console.log('🎉 SUCCESS!\n');
        console.log('📋 Generated files:');
        console.log('  • FolderSummary.txt - Your export guide');
        console.log('  • ExportList_AllParents.txt - All file paths');
        console.log('\n💡 Open FolderSummary.txt to see which folders to export!');
        console.log('💡 Focus on the top 5-10 folders for maximum coverage.');
        
    } catch (error) {
        console.error('\n❌ Error:', error.message);
    }
}

main();