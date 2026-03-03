import { Trend } from 'k6/metrics';
import http from 'k6/http';
import { check, sleep } from 'k6';

// --- Pooled Trends (PrimaryDbContext) ---
const trendPoolSeq = new Trend('db_pool_seq_duration');
const trendPoolConcur = new Trend('db_pool_concur_duration');
const trendSustained = new Trend('db_pool_sustained_duration');

// --- Standard Trends (ReplicaDbContext) ---
const trendStdSeq = new Trend('db_std_seq_duration');
const trendStdConcur = new Trend('db_std_concur_duration');

export const options = {
    stages: [
        { duration: '30s', target: 50 }, // Ramp up to 50 VUs (10 per endpoint)
        { duration: '1m', target: 50 },  // Sustained peak load
        { duration: '20s', target: 0 },  // Cool down
    ],
    thresholds: {
        'http_req_duration': ['p(95)<2500'], // Allowing more for sustained waves
        'db_pool_sustained_duration': ['p(95)<3000'], // High-load threshold
    },
};

const BASE_URL = 'http://192.168.1.9:8080';

export default function () {
    // Split traffic 5 ways
    const vuId = __VU % 5;

    if (vuId === 0) {
        // 1. POOLED - Sequential
        let res = http.post(`${BASE_URL}/api/pool/test/sequential?requests=5&delayMs=20`, null);
        check(res, { 'pool_seq_ok': (r) => r.status === 200 });
        trendPoolSeq.add(res.timings.duration);
    }
    else if (vuId === 1) {
        // 2. POOLED - Concurrent
        let res = http.post(`${BASE_URL}/api/pool/test/concurrent?parallelRequests=5&delayMs=50`, null);
        check(res, { 'pool_concur_ok': (r) => r.status === 200 });
        trendPoolConcur.add(res.timings.duration);
    }
    else if (vuId === 2) {
        // 3. STANDARD - Sequential
        let res = http.post(`${BASE_URL}/api/standard/test/sequential?requests=5&delayMs=20`, null);
        check(res, { 'std_seq_ok': (r) => r.status === 200 });
        trendStdSeq.add(res.timings.duration);
    }
    else if (vuId === 3) {
        // 4. STANDARD - Concurrent
        let res = http.post(`${BASE_URL}/api/standard/test/concurrent?parallelRequests=5&delayMs=50`, null);
        check(res, { 'std_concur_ok': (r) => r.status === 200 });
        trendStdConcur.add(res.timings.duration);
    }
    else {
        // 5. POOLED - Sustained High Load (The Stress Test)
        let res = http.get(`${BASE_URL}/api/pooldiagnostics/sustained-high-load?waves=1&requestsPerWave=10&delayMs=100`);
        check(res, { 'sustained status 200': (r) => r.status === 200 });
        trendSustained.add(res.timings.duration);
    }

    sleep(1);
}


//import http from 'k6/http';
//import { sleep, check } from 'k6';

//// Use container name, not localhost — they're on the same Docker network
//const BASE_URL = 'http://192.168.1.9:8080';

//export const options = {
//    scenarios: {

//        // Phase 1: Warm up — sequential, builds reuse ratio
//        warmup: {
//            executor: 'constant-vus',
//            vus: 1,
//            duration: '30s',
//            startTime: '0s',
//            tags: { phase: 'warmup' },
//        },

//        // Phase 2: Concurrent — expands physical pool instances
//        concurrent: {
//            executor: 'ramping-vus',
//            startVUs: 0,
//            stages: [
//                { duration: '20s', target: 30 },
//                { duration: '30s', target: 30 },
//                { duration: '10s', target: 0 },
//            ],
//            startTime: '35s',
//            tags: { phase: 'concurrent' },
//        },

//        // Phase 3: Sustained high load — holds context 2s, stresses pool
//        sustained_high: {
//            executor: 'constant-vus',
//            vus: 50,
//            duration: '60s',
//            startTime: '100s',
//            tags: { phase: 'sustained_high' },
//        },

//        // Phase 4: Overflow — push past pool size 128
//        overflow: {
//            executor: 'constant-vus',
//            vus: 150,
//            duration: '30s',
//            startTime: '170s',
//            tags: { phase: 'overflow' },
//        },
//    },

//    thresholds: {
//        http_req_duration: ['p(95)<5000'],
//        http_req_failed: ['rate<0.1'],
//    },
//};

//export default function () {
//    const scenario = __ENV.K6_SCENARIO_NAME;

//    if (scenario === 'warmup') {
//        const res = http.post(`${BASE_URL}/api/pool/test/sequential?requests=10&delayMs=50`);
//        check(res, { 'warmup ok': r => r.status === 200 });

//    } else if (scenario === 'concurrent') {
//        const res = http.post(`${BASE_URL}/api/pool/test/concurrent?parallelRequests=10&delayMs=200`);
//        check(res, { 'concurrent ok': r => r.status === 200 });

//    } else if (scenario === 'sustained_high') {
//        const res = http.get(`${BASE_URL}/api/pooldiagnostics/sustained-high-load?waves=1&requestsPerWave=10&delayMs=100`);
//        check(res, { 'sustained ok': r => r.status === 200 });

//    }
//    else if (scenario === 'overflow') {
//        const res = http.get(`${BASE_URL}/api/pooldiagnostics/sustained-high-load?waves=1&requestsPerWave=150&delayMs=50`);
//        check(res, { 'overflow ok': r => r.status === 200 });
//    }

//    sleep(0.5);
//}