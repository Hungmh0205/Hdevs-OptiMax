// System Optimizer Web Dashboard JS (Live Real-Time PowerShell Stream & Real-Time Stats)

let currentEventSource = null;

// Determine Server API Base URL
const SERVER_BASE = window.location.protocol.startsWith('http') ? '' : 'http://localhost:3000';

function addLog(msg, type = 'info') {
    const consoleBody = document.getElementById('consoleLog');
    if (!consoleBody) return;
    const line = document.createElement('div');
    line.className = `log-line ${type}`;
    const time = new Date().toLocaleTimeString();
    line.innerText = msg.startsWith('[') ? msg : `[${time}] ${msg}`;
    consoleBody.appendChild(line);
    consoleBody.scrollTop = consoleBody.scrollHeight;
}

function updateLogTime() {
    const logTimeElem = document.getElementById('logTime');
    if (logTimeElem) {
        logTimeElem.innerText = `THỜI GIAN: ${new Date().toLocaleTimeString()}`;
    }
}
setInterval(updateLogTime, 1000);

// Real-Time System Stats Fetcher
async function fetchSystemStats() {
    try {
        const res = await fetch(`${SERVER_BASE}/api/stats`);
        if (!res.ok) return;
        const data = await res.json();

        // Update RAM Metrics
        const ramUsedVal = document.getElementById('ramUsedVal');
        const ramFreeSub = document.getElementById('ramFreeSub');
        const ramProgress = document.getElementById('ramProgress');

        if (ramUsedVal) ramUsedVal.innerText = `${data.usedGB} GB (${data.ramPct}%)`;
        if (ramFreeSub) ramFreeSub.innerText = `CÒN TRỐNG: ${data.freeGB} GB / ${data.totalGB} GB`;
        if (ramProgress) ramProgress.style.width = `${data.ramPct}%`;

        // Update CPU Metrics
        const cpuLoadVal = document.getElementById('cpuLoadVal');
        const cpuInfoSub = document.getElementById('cpuInfoSub');
        const powerPlanBadge = document.getElementById('powerPlanBadge');
        const cpuProgress = document.getElementById('cpuProgress');

        if (cpuLoadVal) {
            cpuLoadVal.innerText = `${data.cpuPct}% MỨC TẢI`;
            if (data.cpuPct > 80) cpuLoadVal.style.color = 'var(--danger)';
            else if (data.cpuPct > 50) cpuLoadVal.style.color = 'var(--warning)';
            else cpuLoadVal.style.color = 'var(--primary)';
        }
        if (cpuInfoSub) cpuInfoSub.innerText = `${data.cpuModel} (${data.cpuCores} Nhân) | Hoạt động: ${data.uptimeHours} giờ`;
        if (powerPlanBadge) powerPlanBadge.innerText = `${data.powerPlan}`;
        if (cpuProgress) cpuProgress.style.width = `${data.cpuPct}%`;

        // Update Network Speed & Disk Metrics
        const netSpeedVal = document.getElementById('netSpeedVal');
        const diskFreeSub = document.getElementById('diskFreeSub');
        const netTypeBadge = document.getElementById('netTypeBadge');
        const diskProgress = document.getElementById('diskProgress');

        if (netSpeedVal) netSpeedVal.innerText = `TẢI VỀ: ${data.rxSpeed || '0.0 KB/s'}  |  TẢI LÊN: ${data.txSpeed || '0.0 KB/s'}`;
        if (diskFreeSub) diskFreeSub.innerText = `Ổ C: ${data.diskFreeGB} GB TRỐNG / ${data.diskTotalGB} GB | IP: ${data.netIp}`;
        if (netTypeBadge) netTypeBadge.innerText = `${data.netName.toUpperCase()}`;
        if (diskProgress) diskProgress.style.width = `${data.diskUsedPct}%`;

        // Update Admin Badge
        const adminBadge = document.getElementById('adminBadge');
        if (adminBadge) {
            if (data.isAdmin) {
                adminBadge.innerHTML = '<div class="badge-dot"></div><span>QUYỀN HỆ THỐNG: <span style="color: var(--success); font-weight: 700;">QUYỀN ADMIN</span></span>';
            } else {
                adminBadge.innerHTML = '<div class="badge-dot" style="background: var(--warning); box-shadow: 0 0 10px var(--warning);"></div><span>QUYỀN HỆ THỐNG: <span style="color: var(--warning); font-weight: 700;">QUYỀN THƯỜNG</span></span>';
            }
        }
    } catch (e) {
        console.warn('Unable to fetch live system stats:', e);
    }
}

// Poll real-time system stats every 2 seconds
setInterval(fetchSystemStats, 2000);
fetchSystemStats();

function executePowerShellFlag(flag) {
    if (currentEventSource) {
        currentEventSource.close();
        currentEventSource = null;
    }

    addLog(`>>> YÊU CẦU THỰC THI POWERSHELL: SystemOptimizer.ps1 ${flag}`, 'warn');

    const streamUrl = `${SERVER_BASE}/api/run?flag=${encodeURIComponent(flag)}`;

    currentEventSource = new EventSource(streamUrl);

    currentEventSource.onmessage = function (event) {
        try {
            const data = JSON.parse(event.data);
            if (data.type === 'start') {
                addLog(data.text, 'info');
            } else if (data.type === 'stdout' || data.type === 'stderr') {
                let text = data.text;
                if (text.includes('WARNING: Waiting for service')) return;
                let logType = data.type === 'stderr' ? 'err' : 'info';
                if (text.includes('[v]') || text.includes('completed') || text.includes('active!')) {
                    logType = 'success';
                } else if (text.includes('[i]') || text.includes('---')) {
                    logType = 'info';
                } else if (text.includes('[x]') || text.includes('Error') || text.includes('failed')) {
                    logType = 'err';
                } else if (text.includes('=====')) {
                    logType = 'warn';
                }
                addLog(text, logType);
            } else if (data.type === 'done') {
                addLog(data.text, 'success');
                addLog('==================================================', 'warn');
                currentEventSource.close();
                currentEventSource = null;
                fetchSystemStats();
            }
        } catch (e) {
            console.error('Error parsing SSE event', e);
        }
    };

    currentEventSource.onerror = function (err) {
        addLog('[LỖI] Mất kết nối tới Server. Vui lòng mở http://localhost:3000 trên trình duyệt.', 'err');
        if (currentEventSource) {
            currentEventSource.close();
            currentEventSource = null;
        }
    };
}

function runCheck() { executePowerShellFlag('-Check'); }
function runOptimize() { executePowerShellFlag('-Optimize'); }
function runFix() { executePowerShellFlag('-Fix'); }
function runAdvanced() { executePowerShellFlag('-Advanced'); }
function runPro() { executePowerShellFlag('-Pro'); }

function runRestorePoint() {
    addLog('TẠO ĐIỂM KHÔI PHỦC HỆ THỐNG (BẢO VỆ BẢO MẬT)...', 'warn');
    executePowerShellFlag('-RestorePoint');
}

function runCleanRestore() {
    addLog('DỌN DẸP CÁC BẢN SAO RESTORE CŨ (GIẢI PHÓNG DUNG LƯỢNG)...', 'warn');
    executePowerShellFlag('-CleanRestore');
}

function runDeepJunk() {
    addLog('XÓA GPU SHADER CACHE, CACHE TRÌNH DUYỆT & NHẬT KÝ HỆ THỐNG...', 'warn');
    executePowerShellFlag('-DeepJunk');
}

function runStorageExtreme() {
    addLog('TẮT HIBERNATION & KÍCH HOẠT STORAGE SENSE...', 'warn');
    executePowerShellFlag('-StorageExtreme');
}

function runBloatware() {
    addLog('GỠ ỨNG DỤNG UWP RÁC & TẮT TÁC VỤ TELEMETRY...', 'warn');
    executePowerShellFlag('-Bloatware');
}

function runStandbyRAM() {
    addLog('DỌN BỘ NHỚ STANDBY RAM & TRIM WORKING SETS...', 'warn');
    executePowerShellFlag('-StandbyRAM');
}

function runUltra() {
    addLog('THỰC THI TỐI ƯU ULTRA KERNEL (ƯU TIÊN 0x26, 0MS UI & COMPACT OS)...', 'warn');
    executePowerShellFlag('-Ultra');
}

function runMSIMode() {
    addLog('KÍCH HOẠT PCIE DEVICE MESSAGE SIGNALED INTERRUPTS (MSI MODE)...', 'warn');
    executePowerShellFlag('-MSIMode');
}

function runDisableMPO() {
    addLog('TẮT MULTI-PLANE OVERLAY (MPO) CHỐNG GIẬT KHUNG HÌNH DWM...', 'warn');
    executePowerShellFlag('-DisableMPO');
}

function runMultiDriveTrim() {
    addLog('TRIM TẤT CẢ Ổ SSD VÀ TỐI ƯU Ổ HDD TRÊN HỆ THỐNG...', 'warn');
    executePowerShellFlag('-MultiDriveTrim');
}

function runHardenedServices() {
    addLog('TẮT TRIỆT ĐỂ SYSMAIN, TELEMETRY & SEARCH SERVICES (DISABLED)...', 'warn');
    executePowerShellFlag('-HardenedServices');
}

function runPagefileFix() {
    addLog('CẤU HÌNH VIRTUAL MEMORY CỐ ĐỊNH 4096MB CHỐNG TẢI I/O SSD...', 'warn');
    executePowerShellFlag('-PagefileFix');
}

async function fetchAutostartStatus() {
    try {
        const res = await fetch(`${SERVER_BASE}/api/autostart/status`);
        if (!res.ok) return;
        const data = await res.json();
        const autostartBadge = document.getElementById('autostartBadge');
        if (autostartBadge) {
            if (data.enabled) {
                autostartBadge.innerHTML = '<span>TỰ ĐỘNG KHỞI ĐỘNG: <span style="color: var(--success); font-weight: 700;">ĐÃ BẬT</span></span>';
            } else {
                autostartBadge.innerHTML = '<span>TỰ ĐỘNG KHỞI ĐỘNG: <span style="color: var(--warning); font-weight: 700;">ĐÃ TẮT</span></span>';
            }
        }
    } catch (e) {}
}

setInterval(fetchAutostartStatus, 5000);
fetchAutostartStatus();

function runEnableStartup() {
    addLog('KÍCH HOẠT TỰ ĐỘNG TỐI ƯU KHI KHỞI ĐỘNG WINDOWS...', 'warn');
    executePowerShellFlag('-EnableStartup');
}

function runDisableStartup() {
    addLog('TẮT TỰ ĐỘNG TỐI ƯU KHI KHỞI ĐỘNG WINDOWS...', 'warn');
    executePowerShellFlag('-DisableStartup');
}

function runRevert() {
    if (confirm('Bạn có chắc chắn muốn khôi phục tất cả cài đặt Windows về mặc định ban đầu?')) {
        addLog('ĐANG KHÔI PHỦC TẤT CẢ CÀI ĐẶT VỀ MẶC ĐỊNH WINDOWS...', 'warn');
        executePowerShellFlag('-Revert');
    }
}

function runAllInOne() {
    addLog('BẮT ĐẦU TỐI ƯU HÓA CƠ BẢN ALL-IN-ONE...', 'warn');
    executePowerShellFlag('-All');
}

function runExtreme() {
    addLog('BẮT ĐẦU TỐI ƯU HÓA TRIỆT ĐỂ EXTREME 100%...', 'warn');
    executePowerShellFlag('-Extreme');
}
