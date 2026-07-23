const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const os = require('os');

const PORT = 3000;
const PUBLIC_DIR = __dirname;

const mimeTypes = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.ico': 'image/x-icon'
};

let prevCpus = os.cpus();
let cachedPowerPlan = 'ULTIMATE PERFORMANCE';

function updatePowerPlan() {
    try {
        const { exec } = require('child_process');
        exec('powercfg /getactivescheme', (err, stdout) => {
            if (!err && stdout) {
                if (stdout.includes('e9a42b02') || stdout.includes('Ultimate Performance')) {
                    cachedPowerPlan = 'ULTIMATE PERFORMANCE';
                } else if (stdout.includes('8c5e7fda') || stdout.includes('High performance')) {
                    cachedPowerPlan = 'HIGH PERFORMANCE';
                } else if (stdout.includes('381b4222') || stdout.includes('Balanced')) {
                    cachedPowerPlan = 'BALANCED (Power Saving)';
                } else {
                    const match = stdout.match(/\(([^)]+)\)/);
                    cachedPowerPlan = match ? match[1].toUpperCase() : 'CUSTOM PERFORMANCE';
                }
            }
        });
    } catch (e) {}
}
setInterval(updatePowerPlan, 5000);
updatePowerPlan();

function getCpuUsagePct() {
    const currentCpus = os.cpus();
    let idleDiff = 0;
    let totalDiff = 0;
    for (let i = 0; i < currentCpus.length; i++) {
        const prev = prevCpus[i] ? prevCpus[i].times : { user: 0, nice: 0, sys: 0, idle: 0, irq: 0 };
        const curr = currentCpus[i].times;
        const idle = curr.idle - prev.idle;
        const total = (curr.user - prev.user) + (curr.nice - prev.nice) + (curr.sys - prev.sys) + (curr.irq - prev.irq) + idle;
        idleDiff += idle;
        totalDiff += total;
    }
    prevCpus = currentCpus;
    if (totalDiff === 0) return 0;
    return Math.min(100, Math.max(0, Math.round((1 - idleDiff / totalDiff) * 100)));
}

let activeAdapterName = 'Wi-Fi';
let prevNetBytes = { rx: 0, tx: 0, time: Date.now() };
let currentNetSpeed = { rxSpeed: '0.0 KB/s', txSpeed: '0.0 KB/s' };

function getNetworkInfo() {
    const nets = os.networkInterfaces();
    let best = { name: 'Wi-Fi', ip: '127.0.0.1' };
    for (const name of Object.keys(nets)) {
        const isVirtual = name.toLowerCase().includes('vethernet') || name.toLowerCase().includes('radmin') || name.toLowerCase().includes('vmware') || name.toLowerCase().includes('virtualbox') || name.toLowerCase().includes('loopback');
        if (isVirtual) continue;

        for (const net of nets[name]) {
            if (net.family === 'IPv4' && !net.internal) {
                activeAdapterName = name;
                return { name, ip: net.address };
            }
        }
    }
    return best;
}

function updateNetworkSpeed() {
    try {
        const { exec } = require('child_process');
        const netInfo = getNetworkInfo();
        const targetName = netInfo.name || 'Wi-Fi';
        const psCmd = `powershell -ExecutionPolicy Bypass -Command "Get-NetAdapterStatistics | Where-Object Name -like '*${targetName}*' | Select-Object Name, ReceivedBytes, SentBytes | ConvertTo-Json -Compress"`;
        exec(psCmd, (err, stdout) => {
            if (!err && stdout && stdout.trim().length > 0) {
                try {
                    let data = JSON.parse(stdout.trim());
                    if (Array.isArray(data)) {
                        data = data[0];
                    }
                    if (data && data.ReceivedBytes !== undefined) {
                        const rx = parseInt(data.ReceivedBytes, 10);
                        const tx = parseInt(data.SentBytes, 10);
                        const now = Date.now();
                        const dt = (now - prevNetBytes.time) / 1000;

                        if (prevNetBytes.rx > 0 && dt > 0) {
                            const rxBps = Math.max(0, (rx - prevNetBytes.rx) / dt);
                            const txBps = Math.max(0, (tx - prevNetBytes.tx) / dt);

                            const formatSpeed = (bps) => {
                                if (bps >= 1024 * 1024) return (bps / (1024 * 1024)).toFixed(1) + ' MB/s';
                                if (bps >= 1024) return (bps / 1024).toFixed(1) + ' KB/s';
                                return (bps / 1024).toFixed(1) + ' KB/s';
                            };

                            currentNetSpeed = {
                                rxSpeed: formatSpeed(rxBps),
                                txSpeed: formatSpeed(txBps)
                            };
                        }
                        prevNetBytes = { rx, tx, time: now };
                    }
                } catch (e) {}
            }
        });
    } catch (e) {}
}

setInterval(updateNetworkSpeed, 1500);
updateNetworkSpeed();

function getDiskStats() {
    try {
        const stats = fs.statfsSync('C:/');
        const freeBytes = stats.bfree * stats.bsize;
        const totalBytes = stats.blocks * stats.bsize;
        const usedBytes = totalBytes - freeBytes;
        const freeGB = (freeBytes / (1024 * 1024 * 1024)).toFixed(1);
        const totalGB = (totalBytes / (1024 * 1024 * 1024)).toFixed(1);
        const usedGB = (usedBytes / (1024 * 1024 * 1024)).toFixed(1);
        const usedPct = Math.round((usedBytes / totalBytes) * 100);
        return { freeGB, totalGB, usedGB, usedPct };
    } catch (e) {
        return { freeGB: '0', totalGB: '0', usedGB: '0', usedPct: 0 };
    }
}

const server = http.createServer((req, res) => {
    // Enable CORS for all local origins
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', '*');

    if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
    }

    const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost:3000'}`);
    const pathname = parsedUrl.pathname;

    // Real-Time System Stats API Endpoint
    if (pathname === '/api/stats') {
        const totalMem = os.totalmem();
        const freeMem = os.freemem();
        const usedMem = totalMem - freeMem;
        const ramPct = Math.round((usedMem / totalMem) * 100);

        let isAdmin = false;
        try {
            const { execSync } = require('child_process');
            execSync('net session', { stdio: 'ignore' });
            isAdmin = true;
        } catch (e) {}

        const cpuPct = getCpuUsagePct();
        const disk = getDiskStats();
        const net = getNetworkInfo();

        res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(JSON.stringify({
            totalGB: (totalMem / (1024 * 1024 * 1024)).toFixed(2),
            freeGB: (freeMem / (1024 * 1024 * 1024)).toFixed(2),
            usedGB: (usedMem / (1024 * 1024 * 1024)).toFixed(2),
            ramPct: ramPct,
            cpuPct: cpuPct,
            powerPlan: cachedPowerPlan,
            diskFreeGB: disk.freeGB,
            diskTotalGB: disk.totalGB,
            diskUsedGB: disk.usedGB,
            diskUsedPct: disk.usedPct,
            netName: net.name,
            netIp: net.ip,
            rxSpeed: currentNetSpeed.rxSpeed,
            txSpeed: currentNetSpeed.txSpeed,
            hostname: os.hostname(),
            platform: os.platform(),
            cpuCores: os.cpus().length,
            cpuModel: os.cpus()[0] ? os.cpus()[0].model.trim() : 'Intel Processor',
            uptimeHours: (os.uptime() / 3600).toFixed(1),
            isAdmin: isAdmin
        }));
        return;
    }

    // API Autostart Status Endpoint
    if (pathname === '/api/autostart/status') {
        const startupDir = path.join(process.env.APPDATA || '', 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup');
        const vbsPath = path.join(startupDir, 'SystemOptimizerBoot.vbs');
        const isEnabled = fs.existsSync(vbsPath);
        res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(JSON.stringify({ enabled: isEnabled, path: vbsPath }));
        return;
    }

    // API Stream Endpoint (Server-Sent Events for Live PowerShell Output)
    if (pathname === '/api/run') {
        const flag = parsedUrl.searchParams.get('flag') || '-Check';

        res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
            'Access-Control-Allow-Origin': '*'
        });

        const allowedFlags = ['-Check', '-Optimize', '-Fix', '-Advanced', '-Pro', '-RestorePoint', '-CleanRestore', '-DeepJunk', '-StorageExtreme', '-Bloatware', '-StandbyRAM', '-Ultra', '-MSIMode', '-DisableMPO', '-MultiDriveTrim', '-HardenedServices', '-PagefileFix', '-EnableStartup', '-DisableStartup', '-CheckStartup', '-Revert', '-Auto', '-All', '-Extreme'];
        const safeFlag = allowedFlags.includes(flag) ? flag : '-Check';

        res.write(`data: ${JSON.stringify({ type: 'start', text: `[SERVER] Executing PowerShell Optimax.ps1 ${safeFlag}...` })}\n\n`);

        let psArgs = ['-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'Optimax.ps1'), safeFlag];
        const ps = spawn('powershell.exe', psArgs);

        ps.stdout.on('data', (data) => {
            const lines = data.toString('utf8').split(/\r?\n/);
            lines.forEach(line => {
                if (line.trim().length > 0) {
                    res.write(`data: ${JSON.stringify({ type: 'stdout', text: line })}\n\n`);
                }
            });
        });

        ps.stderr.on('data', (data) => {
            const lines = data.toString('utf8').split(/\r?\n/);
            lines.forEach(line => {
                if (line.trim().length > 0) {
                    res.write(`data: ${JSON.stringify({ type: 'stderr', text: line })}\n\n`);
                }
            });
        });

        ps.on('close', (code) => {
            res.write(`data: ${JSON.stringify({ type: 'done', code: code, text: `[SERVER] Process finished with exit code ${code}.` })}\n\n`);
            res.end();
        });

        req.on('close', () => {
            ps.kill();
        });
        return;
    }

    // Static File Server
    let filePath = path.join(PUBLIC_DIR, pathname === '/' ? 'index.html' : pathname);
    const ext = path.extname(filePath);
    const contentType = mimeTypes[ext] || 'text/plain';

    fs.readFile(filePath, (err, content) => {
        if (err) {
            if (err.code === 'ENOENT') {
                res.writeHead(404, { 'Content-Type': 'text/plain' });
                res.end('404 Not Found');
            } else {
                res.writeHead(500, { 'Content-Type': 'text/plain' });
                res.end(`500 Server Error: ${err.code}`);
            }
        } else {
            res.writeHead(200, { 'Content-Type': contentType });
            res.end(content);
        }
    });
});

server.listen(PORT, '0.0.0.0', () => {
    console.log(`[OPTIMAX SERVER] Running live at http://localhost:${PORT}`);
});
