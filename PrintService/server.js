'use strict';
/**
 * ZPL Print Service — Node.js v3
 * ─────────────────────────────────────────────────────────────────────────────
 * Nhận ZPL + IP từ trình duyệt (không phải từ cloud server),
 * gửi lệnh in qua TCP/IP raw đến máy in Zebra trong mạng nội bộ.
 * Hỗ trợ thêm: in qua USB (Windows + Linux/Mac)
 *
 * Chạy tại: http://localhost:8021
 * ─────────────────────────────────────────────────────────────────────────────
 */

const express = require('express');
const cors    = require('cors');
const net     = require('net');
const fs      = require('fs');
const path    = require('path');
const { exec, execFile } = require('child_process');
const os      = require('os');

const app  = express();
const PORT = 8021;

// ── CORS ──────────────────────────────────────────────────────────────────────
app.use(cors({
  origin: '*',
  methods: ['GET', 'POST', 'OPTIONS'],
  allowedHeaders: ['Content-Type']
}));
app.use(express.json({ limit: '5mb' }));

// ── Util: gửi ZPL qua TCP raw socket ─────────────────────────────────────────
function printViaTcp(printerIp, printerPort, zplData) {
  return new Promise((resolve, reject) => {
    const t0     = Date.now();
    const socket = new net.Socket();
    const buf    = Buffer.from(zplData, 'utf8');

    socket.setTimeout(6000);

    socket.connect(printerPort, printerIp, () => {
      console.log(`  [TCP] Kết nối thành công → ${printerIp}:${printerPort}`);
      socket.write(buf, (writeErr) => {
        if (writeErr) {
          socket.destroy();
          return reject(new Error(`Lỗi ghi socket: ${writeErr.message}`));
        }
        setTimeout(() => {
          socket.end();
          resolve({ bytesSent: buf.length, duration: Date.now() - t0 });
        }, 200);
      });
    });

    socket.on('timeout', () => {
      socket.destroy();
      reject(new Error(`Timeout (6s) khi kết nối đến ${printerIp}:${printerPort}`));
    });

    socket.on('error', (err) => reject(new Error(`TCP error: ${err.message}`)));
  });
}

// ── Util: lưu ZPL ra file để debug / để in USB ───────────────────────────────
function logZplFile(zpl, prefix = 'print') {
  try {
    const dir  = path.join(__dirname, 'zpl-logs');
    fs.mkdirSync(dir, { recursive: true });
    const ts   = new Date().toISOString().replace(/[:.]/g, '-');
    const safe = prefix.replace(/[^a-zA-Z0-9_\-]/g, '_');
    const file = path.join(dir, `${safe}_${ts}.zpl`);
    fs.writeFileSync(file, zpl, 'utf8');
    return file;
  } catch { return null; }
}

// ── Util: lấy danh sách máy in USB theo OS ───────────────────────────────────
function listUsbPrinters() {
  return new Promise((resolve) => {
    const platform = os.platform();

    if (platform === 'win32') {
      // Windows: dùng PowerShell để lấy printer list
      const cmd = `powershell -NoProfile -Command "Get-Printer | Select-Object -ExpandProperty Name | ConvertTo-Json"`;
      exec(cmd, { timeout: 5000 }, (err, stdout) => {
        if (err) {
          console.warn('[USB] PowerShell lỗi:', err.message);
          // Fallback: wmic
          exec('wmic printer get name /format:list', { timeout: 5000 }, (err2, out2) => {
            if (err2) return resolve([]);
            const printers = out2
              .split('\n')
              .map(l => l.replace(/^Name=/, '').trim())
              .filter(l => l.length > 0);
            resolve(printers);
          });
          return;
        }
        try {
          let parsed = JSON.parse(stdout.trim());
          if (!Array.isArray(parsed)) parsed = [parsed];
          resolve(parsed.filter(Boolean));
        } catch {
          const printers = stdout.split('\n').map(l => l.trim()).filter(Boolean);
          resolve(printers);
        }
      });

    } else if (platform === 'linux') {
      // Linux: dùng lpstat hoặc đọc /dev/usb/lp*
      exec('lpstat -a 2>/dev/null', { timeout: 4000 }, (err, stdout) => {
        let lpPrinters = [];
        if (!err && stdout.trim()) {
          lpPrinters = stdout
            .split('\n')
            .map(l => l.split(' ')[0].trim())
            .filter(Boolean);
        }

        // Thêm USB raw devices
        const usbDevs = [];
        try {
          const usbDir = '/dev/usb';
          if (fs.existsSync(usbDir)) {
            fs.readdirSync(usbDir)
              .filter(f => f.startsWith('lp'))
              .forEach(f => usbDevs.push(`USB RAW: /dev/usb/${f}`));
          }
        } catch { /* ignore */ }

        resolve([...new Set([...lpPrinters, ...usbDevs])]);
      });

    } else if (platform === 'darwin') {
      // macOS: dùng lpstat
      exec('lpstat -a 2>/dev/null', { timeout: 4000 }, (err, stdout) => {
        if (err || !stdout.trim()) return resolve([]);
        const printers = stdout
          .split('\n')
          .map(l => l.split(' ')[0].trim())
          .filter(Boolean);
        resolve(printers);
      });

    } else {
      resolve([]);
    }
  });
}

// ── Util: in qua USB ──────────────────────────────────────────────────────────
function printViaUsb(printerName, zplData) {
  return new Promise((resolve, reject) => {
    const t0  = Date.now();
    const buf = Buffer.from(zplData, 'utf8');

    if (os.platform() === 'win32') {
      const tmpFile = path.join(os.tmpdir(), `zpl_${Date.now()}.zpl`);
      fs.writeFileSync(tmpFile, buf);

      // copy /b gửi raw bytes thẳng đến shared printer
      const cmd = `copy /b "${tmpFile}" "\\\\localhost\\${printerName}"`;
      console.log(`  [USB] ${cmd}`);

      exec(cmd, { timeout: 10000 }, (err, stdout, stderr) => {
        try { fs.unlinkSync(tmpFile); } catch {}
        console.log(`  [USB] stdout: ${stdout.trim()}`);
        console.log(`  [USB] stderr: ${stderr.trim()}`);
        if (err) return reject(new Error(`copy /b lỗi: ${err.message} | ${stderr}`));
        resolve({ bytesSent: buf.length, duration: Date.now() - t0 });
      });

    } else {
      // Linux/Pi: ghi thẳng ra /dev/usb/lp0
      const devPath = printerName.startsWith('USB RAW:')
        ? printerName.replace('USB RAW: ', '').trim()
        : null;

      if (devPath) {
        try {
          fs.writeFileSync(devPath, buf);
          return resolve({ bytesSent: buf.length, duration: Date.now() - t0 });
        } catch (e) {
          return reject(new Error(`Không ghi được vào ${devPath}: ${e.message}`));
        }
      }

      // CUPS fallback
      const tmpFile = path.join(os.tmpdir(), `zpl_${Date.now()}.zpl`);
      fs.writeFileSync(tmpFile, buf);
      execFile('lpr', ['-P', printerName, '-l', tmpFile], { timeout: 10000 }, (err) => {
        try { fs.unlinkSync(tmpFile); } catch {}
        if (err) return reject(new Error(`lpr lỗi: ${err.message}`));
        resolve({ bytesSent: buf.length, duration: Date.now() - t0 });
      });
    }
  });
}

// ── POST /print (TCP/IP) ──────────────────────────────────────────────────────
app.post('/print', async (req, res) => {
  const { zpl, printerIp, printerPort = 9100 } = req.body ?? {};

  if (!zpl || typeof zpl !== 'string')
    return res.status(400).json({ error: 'Thiếu trường "zpl".' });
  if (!printerIp || typeof printerIp !== 'string')
    return res.status(400).json({ error: 'Thiếu trường "printerIp".' });
  // if (!zpl.trim().startsWith('^XA'))
  //   return res.status(400).json({ error: 'ZPL không hợp lệ — phải bắt đầu bằng ^XA.' });

  const port = parseInt(printerPort, 10);
  if (isNaN(port) || port < 1 || port > 65535)
    return res.status(400).json({ error: 'printerPort không hợp lệ.' });

  console.log(`\n► [TCP PRINT] ${new Date().toLocaleTimeString()}`);
  console.log(`  IP    : ${printerIp}:${port}`);
  console.log(`  ZPL   : ${zpl.length} ký tự`);

  const saved = logZplFile(zpl, printerIp.replace(/\./g, '_'));
  if (saved) console.log(`  Log   : ${saved}`);

  try {
    const result = await printViaTcp(printerIp, port, zpl);
    console.log(`  ✓ Gửi thành công — ${result.bytesSent} bytes / ${result.duration}ms`);
    return res.json({
      success  : true,
      message  : 'Đã in thành công qua TCP/IP',
      bytesSent: result.bytesSent,
      duration : result.duration
    });
  } catch (err) {
    console.error(`  ✗ Lỗi: ${err.message}`);
    return res.status(500).json({
      error: err.message,
      tip  : 'Kiểm tra: IP máy in đúng chưa? Máy in có bật không? Có cùng mạng LAN không? Cổng 9100 có mở không?'
    });
  }
});

// ── GET /usb-printers ─────────────────────────────────────────────────────────
app.get('/usb-printers', async (_req, res) => {
  console.log(`\n► [USB LIST] ${new Date().toLocaleTimeString()} — platform: ${os.platform()}`);
  try {
    const printers = await listUsbPrinters();
    console.log(`  Tìm thấy ${printers.length} máy in:`, printers);
    return res.json({ success: true, printers, platform: os.platform() });
  } catch (err) {
    console.error(`  ✗ Lỗi: ${err.message}`);
    return res.status(500).json({ error: err.message, printers: [] });
  }
});

// ── POST /print-usb ───────────────────────────────────────────────────────────
// Body: { zpl: string, printerName: string }
app.post('/print-usb', async (req, res) => {
  const { zpl, printerName } = req.body ?? {};

  if (!zpl || typeof zpl !== 'string')
    return res.status(400).json({ error: 'Thiếu trường "zpl".' });
  if (!printerName || typeof printerName !== 'string')
    return res.status(400).json({ error: 'Thiếu trường "printerName".' });
  if (!zpl.trim().startsWith('^XA'))
    return res.status(400).json({ error: 'ZPL không hợp lệ — phải bắt đầu bằng ^XA.' });

  console.log(`\n► [USB PRINT] ${new Date().toLocaleTimeString()}`);
  console.log(`  Printer: ${printerName}`);
  console.log(`  ZPL    : ${zpl.length} ký tự`);

  const saved = logZplFile(zpl, 'usb');
  if (saved) console.log(`  Log    : ${saved}`);

  try {
    const result = await printViaUsb(printerName, zpl);
    console.log(`  ✓ Gửi thành công — ${result.bytesSent} bytes / ${result.duration}ms`);
    return res.json({
      success    : true,
      message    : `Đã in thành công qua USB`,
      printerName: printerName,
      bytesSent  : result.bytesSent,
      duration   : result.duration
    });
  } catch (err) {
    console.error(`  ✗ Lỗi: ${err.message}`);
    return res.status(500).json({
      error: err.message,
      tip  : 'Kiểm tra: Máy in có bật không? Driver đã cài chưa? Tên máy in có đúng không?'
    });
  }
});

// ── GET /health ───────────────────────────────────────────────────────────────
app.get('/health', (_req, res) => {
  res.json({
    status   : 'ok',
    service  : 'ZPL Print Service',
    port     : PORT,
    platform : os.platform(),
    uptime   : Math.floor(process.uptime()),
    timestamp: new Date().toISOString()
  });
});

// ── GET /ping-printer?ip=...&port=... ─────────────────────────────────────────
app.get('/ping-printer', (req, res) => {
  const { ip, port = 9100 } = req.query;
  if (!ip) return res.status(400).json({ error: 'Thiếu ?ip=...' });

  const tcpPort = parseInt(port, 10);
  const socket  = new net.Socket();
  let done = false;

  socket.setTimeout(3000);

  socket.connect(tcpPort, ip, () => {
    if (done) return; done = true;
    socket.destroy();
    res.json({ reachable: true,  ip, port: tcpPort });
  });

  const fail = (msg) => {
    if (done) return; done = true;
    res.json({ reachable: false, ip, port: tcpPort, reason: msg });
  };

  socket.on('timeout', () => { socket.destroy(); fail('Timeout 3s'); });
  socket.on('error',   (e) => fail(e.message));
});

// ── Start ─────────────────────────────────────────────────────────────────────
app.listen(PORT, () => {
  console.log('');
  console.log('  ┌──────────────────────────────────────────┐');
  console.log('  │   ZPL Print Service — localhost           │');
  console.log(`  │   Port: ${PORT}  |  OS: ${os.platform().padEnd(10)}          │`);
  console.log('  ├──────────────────────────────────────────┤');
  console.log('  │  POST /print          → In TCP/IP        │');
  console.log('  │  POST /print-usb      → In USB           │');
  console.log('  │  GET  /usb-printers   → Liệt kê USB      │');
  console.log('  │  GET  /health         → Kiểm tra         │');
  console.log('  │  GET  /ping-printer   → Test TCP         │');
  console.log('  └──────────────────────────────────────────┘');
  console.log('');
});