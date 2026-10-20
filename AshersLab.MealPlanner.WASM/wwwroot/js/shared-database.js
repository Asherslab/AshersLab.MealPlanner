// One browser database shared by every window of the app in this browser profile.
//
// SqliteWasmBlazor keeps the database in OPFS through access handles that only one worker may hold, so
// on its own a second window cannot open it. This script stands in for the Worker that SqliteWasmBlazor's
// bridge creates. LocalDatabase.StartAsync imports it - with retries, because a script that fails to load
// fails silently - before it lets SqliteWasmBlazor start:
//
//   - every window gets a client that looks like that Worker to the bridge;
//   - the window holding the "mealplanner-db-leader" Web Lock also runs the one real worker;
//   - the others send their requests to it over a BroadcastChannel and get the answers back;
//   - when that window closes its lock passes to another window, which starts a worker and takes over.
//
// Every window shares one SQLite connection, so the leader gates transactions: once a window's BEGIN is
// sent, the other windows' statements wait until its COMMIT or ROLLBACK. See
// docs/modules/sync/browser-database.md. Tied to SqliteWasmBlazor's bridge <-> worker messages; recheck
// this file when upgrading that package.
(function (root) {
    'use strict';

    // ---- Pure rules, unit-tested in AshersLab.MealPlanner.Tests/Js --------------------------------------

    /** What a statement means to the gate: begin | commit | rollback | write | read. */
    function classify(sql) {
        const s = String(sql ?? '').replace(/^(\s|--[^\n]*\n|\/\*[\s\S]*?\*\/)+/, '').toUpperCase();
        if (/^BEGIN\b/.test(s)) return 'begin';
        if (/^(COMMIT|END)\b/.test(s)) return 'commit';
        // ROLLBACK TO SAVEPOINT undoes part of a transaction and leaves it open - EF Core uses savepoints.
        if (/^ROLLBACK\b/.test(s)) return /^ROLLBACK\s+(TRANSACTION\s+)?TO\b/.test(s) ? 'write' : 'rollback';
        if (/^(INSERT|UPDATE|DELETE|REPLACE|CREATE|DROP|ALTER)\b/.test(s)) return 'write';
        return 'read';
    }

    /**
     * Orders requests from several windows onto one connection. A request is { tab, kind }; `send` puts
     * it on the worker. Requests are sent at once unless another window owns an open transaction, in
     * which case they wait, in arrival order, until that transaction ends.
     */
    class TransactionGate {
        constructor(send) {
            this.send = send;
            this.owner = null;
            this.waiting = [];
        }

        submit(request) {
            if (this.owner !== null && this.owner !== request.tab) {
                this.waiting.push(request);
                return;
            }
            this.dispatch(request);
        }

        dispatch(request) {
            // Owned from the moment BEGIN is sent, not when it answers: anything another window sends in
            // between would otherwise run inside this transaction.
            if (request.kind === 'begin') this.owner = request.tab;
            this.send(request);
        }

        /**
         * A request answered. Returns whether the database changed in a way other windows should hear
         * about: a committed transaction, or a write outside any transaction.
         */
        completed(request, success) {
            let changed = false;
            if (request.kind === 'begin' && !success && this.owner === request.tab) {
                this.owner = null;
            } else if (request.kind === 'commit' && this.owner === request.tab) {
                // A failed COMMIT leaves the transaction open; EF then sends ROLLBACK.
                if (success) { this.owner = null; changed = true; }
            } else if (request.kind === 'rollback' && this.owner === request.tab) {
                this.owner = null;
            } else if (request.kind === 'write' && success && this.owner !== request.tab) {
                changed = true;
            }
            this.drain();
            return changed;
        }

        /** A window closed. Returns true if it left a transaction open, which the caller must roll back. */
        tabGone(tab) {
            this.waiting = this.waiting.filter(r => r.tab !== tab);
            if (this.owner !== tab) return false;
            this.owner = null;
            this.drain();
            return true;
        }

        drain() {
            const queued = this.waiting;
            this.waiting = [];
            for (const request of queued) this.submit(request);
        }
    }

    root.MealPlannerSharedDatabase = { classify, TransactionGate };

    if (typeof window === 'undefined' || !root.navigator?.locks || typeof BroadcastChannel === 'undefined') return;
    // Loaded again by a retry after a load that did run: one Worker patch only.
    if (root.mealPlannerDatabase) return;

    // ---- The browser half ----------------------------------------------------------------------------

    const CHANNEL = 'mealplanner-db';
    const LEADER_LOCK = 'mealplanner-db-leader';
    const TAB_LOCK = 'mealplanner-db-tab-';
    const WORKER_FILE = 'sqlite-wasm-worker.js';
    const START_ATTEMPTS = 6;     // 0.25 + 0.5 + 1 + 2 + 4 s between them: about 8 s before giving up
    const forever = () => new Promise(() => {});
    const RealWorker = root.Worker;

    // What this window's shim has done, newest last - read it from the console or a test when a window
    // never gets a database. Bounded; holds no SQL or data.
    const events = [];
    function note(what) {
        events.push(`${Math.round(performance.now())}ms ${what}`);
        if (events.length > 50) events.shift();
    }

    function errorFor(id, message) {
        return { id, data: { success: false, error: message } };
    }

    const succeeded = data => data.binary === true || data.rawBinary === true || data.data?.success === true;

    /** The real worker, in the window holding the leader lock. */
    class Leader {
        constructor(client) {
            this.client = client;
            this.routes = new Map();
            this.nextId = 1;
            this.watched = new Set();
            this.databases = new Map();  // window -> the database it last used, for rolling back after it
            this.gate = new TransactionGate(r => this.post(r));
            this.isReady = false;
            this.attempt = 0;
            this.ready = new Promise(resolve => this.resolveReady = resolve);
            this.startWorker();
        }

        /**
         * Start the real worker, again if it fails before it is ready. The Web Lock and the OPFS access
         * handles are let go by different parts of a closing window, so the window that takes over can
         * get the lock while the old worker still holds the files - its pool install then fails. Without
         * a retry no window in the profile would ever get a database until every one reloaded.
         */
        startWorker() {
            this.attempt++;
            this.worker = new RealWorker(this.client.workerUrl, this.client.workerOptions);
            this.worker.onmessage = e => this.fromWorker(e.data);
            this.worker.onerror = e => {
                if (!this.isReady) { e.preventDefault?.(); this.startFailed(e.message ?? 'worker script failed to load'); }
                else this.client.onerror?.(e);
            };
            this.worker.postMessage(this.client.initMessage);
            if (this.client.loggingMessage) this.worker.postMessage(this.client.loggingMessage);
        }

        startFailed(reason) {
            this.worker.terminate();
            if (this.attempt >= START_ATTEMPTS) {
                note(`worker failed ${this.attempt} times, giving up: ${reason}`);
                this.client.workerFailed({ type: 'error', error: reason });
                return;
            }
            const wait = 250 * 2 ** (this.attempt - 1);
            note(`worker failed (attempt ${this.attempt}), retrying in ${wait}ms: ${reason}`);
            setTimeout(() => this.startWorker(), wait);
        }

        /** A request from any window, including this one. */
        submit(tab, message, transfer) {
            if (tab !== this.client.tabId) this.watch(tab);
            if (message.data?.database) this.databases.set(tab, message.data.database);
            const kind = message.data?.type === 'execute' ? classify(message.data.sql) : 'read';
            this.gate.submit({ tab, kind, message, transfer });
        }

        post(request) {
            const id = this.nextId++;
            this.routes.set(id, request);
            this.worker.postMessage({ ...request.message, id }, request.transfer ?? []);
        }

        fromWorker(data) {
            if (data.type === 'ready') { note(`worker ready (attempt ${this.attempt})`); this.isReady = true; this.resolveReady(); return; }
            if (data.type === 'error') {
                if (!this.isReady) this.startFailed(data.error);
                else { note(`worker error: ${data.error}`); this.client.workerFailed(data); }
                return;
            }
            if (data.id === undefined) { this.client.deliver(data); return; }   // streams: this window's own

            const request = this.routes.get(data.id);
            if (!request) return;
            this.routes.delete(data.id);

            const changed = this.gate.completed(request, succeeded(data));
            if (changed) this.client.announceChange(request.tab);
            if (request.tab === null) return;                                    // the leader's own ROLLBACK

            const answer = { ...data, id: request.message.id };
            if (request.tab === this.client.tabId) this.client.deliver(answer);
            else this.client.channel.postMessage({ t: 'res', to: request.tab, msg: answer });
        }

        /** Learn when a follower window closes: its own lock becomes free. */
        watch(tab) {
            if (this.watched.has(tab)) return;
            this.watched.add(tab);
            navigator.locks.request(TAB_LOCK + tab, () => {
                this.watched.delete(tab);
                const database = this.databases.get(tab);
                this.databases.delete(tab);
                if (this.gate.tabGone(tab) && database) {
                    this.gate.submit({ tab: null, kind: 'rollback',
                        message: { data: { type: 'execute', database, sql: 'ROLLBACK', parameters: {} } } });
                }
            });
        }
    }

    /** What SqliteWasmBlazor's bridge believes is its Worker. One per window. */
    class Client {
        constructor(url, options) {
            this.workerUrl = url;
            this.workerOptions = options;
            this.tabId = crypto.randomUUID();
            this.onmessage = null;
            this.onerror = null;
            this.leaderId = null;        // the window whose worker answers, once it is ready
            this.leader = null;          // set in the window that is the leader
            this.bridgeReady = false;
            this.pending = new Map();    // request id -> { leaderId, kind } sent and not yet answered
            this.queued = [];            // requests waiting for a leader
            this.transaction = 'none';   // none | open | broken
            this.transactionLeader = null;
            this.channel = new BroadcastChannel(CHANNEL);
            this.channel.onmessage = e => this.fromChannel(e.data);
            root.mealPlannerDatabase.client = this;
        }

        // --- Worker surface the bridge uses ---
        addEventListener(type, listener) { if (type === 'message') this.onmessage = listener; else if (type === 'error') this.onerror = listener; }
        removeEventListener() {}
        terminate() {}

        postMessage(message, transfer) {
            if (message?.type === 'init' && 'baseHref' in message) {
                this.initMessage = message;
                this.start();
                return;
            }
            if (message?.type === 'configureLogging') {
                this.loggingMessage = message;
                this.leader?.worker.postMessage(message);
                return;
            }
            if (typeof message?.streamId === 'number') {
                // Exports and imports stage through OPFS in the worker's window; only that window may.
                if (this.leader) this.leader.worker.postMessage(message, transfer ?? []);
                else this.deliver({ streamId: message.streamId, streamError: true,
                    error: 'Export and import only work in the window holding the database.' });
                return;
            }
            this.request(message, transfer);
        }

        // --- Requests from this window ---
        request(message, transfer) {
            const kind = message.data?.type === 'execute' ? classify(message.data.sql) : 'read';

            // The transaction this window opened was on a leader that has gone: its BEGIN went with it. Fail
            // every statement until the transaction ends, or they would run - and commit - one by one.
            if (this.transaction === 'broken') {
                if (kind === 'commit' || kind === 'rollback') this.transaction = 'none';
                this.deliver(errorFor(message.id, 'The window holding the database closed during this change. Try again.'));
                return;
            }

            if (this.leaderId === null) { this.queued.push({ message, transfer, kind }); return; }
            this.send(message, transfer, kind);
        }

        send(message, transfer, kind) {
            this.pending.set(message.id, { leaderId: this.leaderId, kind });
            if (this.leader) this.leader.submit(this.tabId, message, transfer);
            else this.channel.postMessage({ t: 'req', from: this.tabId, to: this.leaderId, msg: message });
        }

        /** An answer for this window, from its own leader or another window's. */
        deliver(data) {
            if (data.id !== undefined) {
                const sent = this.pending.get(data.id);
                this.pending.delete(data.id);
                if (sent && succeeded(data)) {
                    if (sent.kind === 'begin') { this.transaction = 'open'; this.transactionLeader = sent.leaderId; }
                    else if (sent.kind === 'commit' || sent.kind === 'rollback') this.transaction = 'none';
                }
            }
            this.onmessage?.({ data });
        }

        // --- Leadership ---
        start() {
            navigator.locks.request(TAB_LOCK + this.tabId, forever);
            navigator.locks.request(LEADER_LOCK, async () => {
                note('leader lock granted, starting worker');
                this.leader = new Leader(this);
                await this.leader.ready;
                this.channel.postMessage({ t: 'leader', id: this.tabId });
                this.leaderIs(this.tabId);
                return forever();
            });
            addEventListener('pagehide', () => { if (this.leader) this.channel.postMessage({ t: 'leader-gone', id: this.tabId }); });
            this.channel.postMessage({ t: 'hello', from: this.tabId });
            note(`started as ${this.tabId.slice(0, 8)}, said hello`);
        }

        leaderIs(id) {
            if (this.leaderId === id) return;
            note(`leader is ${id === this.tabId ? 'this window' : id.slice(0, 8)}`);
            this.leaderId = id;

            // Sent to a leader that has gone: nobody can say whether they ran, so they fail.
            for (const [requestId, sent] of [...this.pending]) {
                if (sent.leaderId === id) continue;
                this.pending.delete(requestId);
                this.onmessage?.({ data: errorFor(requestId, 'The window holding the database closed. Try again.') });
            }
            if (this.transaction === 'open' && this.transactionLeader !== id) this.transaction = 'broken';

            if (!this.bridgeReady) {
                this.bridgeReady = true;
                this.onmessage?.({ data: { type: 'ready' } });
            }
            const queued = this.queued;
            this.queued = [];
            for (const q of queued) this.request(q.message, q.transfer);
        }

        workerFailed(data) {
            this.onmessage?.({ data });
            this.channel.postMessage({ t: 'worker-error', data });
        }

        announceChange(fromTab) {
            this.channel.postMessage({ t: 'changed', from: fromTab });
            if (fromTab !== this.tabId) dispatchEvent(new CustomEvent('mealplanner-db-changed'));
        }

        fromChannel(m) {
            switch (m.t) {
                case 'hello':
                    if (this.leader && this.leaderId === this.tabId) {
                        note(`answered hello from ${m.from.slice(0, 8)}`);
                        this.channel.postMessage({ t: 'leader', id: this.tabId });
                    }
                    break;
                case 'leader':
                    if (!this.leader) this.leaderIs(m.id);
                    break;
                case 'leader-gone':
                    if (this.leaderId === m.id) this.leaderId = null;
                    break;
                case 'req':
                    if (this.leader && m.to === this.tabId) this.leader.submit(m.from, m.msg);
                    else if (this.leader && m.to !== this.tabId) {
                        // Meant for a leader that has gone; tell the sender who leads now.
                        this.channel.postMessage({ t: 'leader', id: this.tabId });
                    }
                    break;
                case 'res':
                    if (m.to === this.tabId) this.deliver(m.msg);
                    break;
                case 'changed':
                    if (m.from !== this.tabId) dispatchEvent(new CustomEvent('mealplanner-db-changed'));
                    break;
                case 'worker-error':
                    if (!this.leader) this.onmessage?.({ data: m.data });
                    break;
            }
        }
    }

    root.mealPlannerDatabase = {
        client: null,
        events,
        /** "leader" when this window runs the database, "follower" when another window does, null before. */
        role() {
            const c = this.client;
            if (!c || c.leaderId === null) return null;
            return c.leaderId === c.tabId ? 'leader' : 'follower';
        },
        /** Calls dotnetRef.OnChangedElsewhere() whenever another window commits a change. */
        subscribe(dotnetRef) {
            addEventListener('mealplanner-db-changed', () => dotnetRef.invokeMethodAsync('OnChangedElsewhere'));
        },
    };

    root.Worker = class extends RealWorker {
        constructor(url, options) {
            if (String(url).endsWith(WORKER_FILE)) return new Client(url, options);
            super(url, options);
        }
    };
})(globalThis);
