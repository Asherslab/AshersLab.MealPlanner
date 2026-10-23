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
//   - when that window closes its lock passes to another window, which starts a worker and takes over;
//   - when it is hidden while another window is in view, it hands the database over the same way - Safari
//     suspends a hidden tab, and a suspended leader keeps the lock and the files but answers nobody.
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

    /**
     * Removes each of `names` from an OPFS directory, waiting for whatever still holds its files to let go: a
     * worker just terminated - this window's or another's - keeps its access handles for a moment, and removing
     * a file with an open handle fails. An entry that is not there is already gone. Throws once `attempts` have
     * failed, about 10 s with the default waits.
     */
    async function removeEntries(directory, names, wait, attempts = 8) {
        for (const name of names) {
            for (let attempt = 1; ; attempt++) {
                try {
                    await directory.removeEntry(name, { recursive: true });
                    break;
                } catch (e) {
                    if (e?.name === 'NotFoundError') break;
                    if (attempt >= attempts) throw e;
                    await wait(Math.min(250 * 2 ** (attempt - 1), 2000));
                }
            }
        }
    }

    /**
     * Whether this window has waited `limit` ms on another one: for a start's turn, for a leader to be announced, or
     * for an answer to a request it sent another window. Each `since` is a performance.now() time, or null for not
     * waiting. A window that leads waits on nobody - its own worker starting is not another window's fault.
     */
    function waitingOnAnotherWindow({ now, leading, startSince, leaderlessSince, sentSince }, limit) {
        const since = [startSince, leading ? null : leaderlessSince, ...(leading ? [] : sentSince)].filter(s => s !== null);
        return since.some(s => now - s >= limit);
    }

    root.MealPlannerSharedDatabase = { classify, TransactionGate, removeEntries, waitingOnAnotherWindow };

    if (typeof window === 'undefined' || !root.navigator?.locks || typeof BroadcastChannel === 'undefined') return;
    // Loaded again by a retry after a load that did run: one Worker patch only.
    if (root.mealPlannerDatabase) return;

    // ---- The browser half ----------------------------------------------------------------------------

    const CHANNEL = 'mealplanner-db';
    const LEADER_LOCK = 'mealplanner-db-leader';
    const TAB_LOCK = 'mealplanner-db-tab-';
    const RESET_LOCK = 'mealplanner-db-reset';
    const START_LOCK = 'mealplanner-db-start';
    // SqliteWasmBlazor's SAHPool directory - every database file - and its export staging, at the OPFS root.
    const DATABASE_ENTRIES = ['databases', 'export-staging'];
    const RESET_TAKE_OVER_MS = 20_000;
    const WORKER_FILE = 'sqlite-wasm-worker.js';
    const START_ATTEMPTS = 6;     // 0.25 + 0.5 + 1 + 2 + 4 s between them: about 8 s before giving up
    // A hidden leader handing over waits this long at most for what is in flight and an open transaction to end;
    // anything still unanswered then fails "try again" in its window, as when a leader closes.
    const HAND_OVER_DRAIN_MS = 10_000;
    // Longer than a leader's worker takes to start through all its retries, so a hand-over is never mistaken for it.
    const WAITING_LIMIT_MS = 10_000;
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
            this.handedOver = new Promise(resolve => this.resolveHandedOver = resolve);
            this.leaving = false;
            this.watching = new AbortController();
            this.opened = new Set();     // databases this worker has open
            this.opening = null;         // while one is being opened: the requests that came meanwhile, in order
            this.startWorker();
        }

        /**
         * Start the real worker, again if it fails before it is ready. The Web Lock and the OPFS access
         * handles are let go by different parts of a closing window, so the window that takes over can
         * get the lock while the old worker still holds the files - its pool install then fails. Without
         * a retry no window in the profile would ever get a database until every one reloaded.
         */
        startWorker() {
            if (this.stopped) return;
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

        /** For a reset: let go of the database files. Nothing is answered after this; the window reloads. */
        stop() {
            this.stopped = true;
            this.worker.terminate();
            note('worker stopped for a reset');
        }

        /**
         * Let go of the database for another window, losing nothing. From now on only an open transaction's own
         * statements run; everything else is dropped unrun. Once nothing is in flight - or HAND_OVER_DRAIN_MS have
         * passed - `gone(clean)` says so while this window still can, the worker stops, which frees the files, and
         * `handedOver` resolves, which gives back the lock.
         *
         * `clean` is nothing in flight: then every request a window still waits on was never run, and it sends them
         * all again to the next leader. Not simply fail what is unanswered, as when a leader closes: a hidden window
         * hands over just as another opens - a background tab is hidden - and a failed request fails the whole start.
         */
        handOver(gone) {
            if (this.leaving) return;
            this.leaving = true;
            const dropped = this.gate.waiting.length;
            this.gate.waiting = [];
            const owner = this.gate.owner;
            note(`handing over: ${this.routes.size} in flight, ${dropped} held back, `
                + `transaction ${owner === null ? 'none' : owner === this.client.tabId ? 'this window\'s' : owner.slice(0, 8) + '\'s'}`);
            this.leave = () => {
                if (this.stopped) return;
                this.stopped = true;
                gone(this.routes.size === 0);
                this.watching.abort();
                this.worker.terminate();
                note(`worker stopped, handed over${this.routes.size ? ` with ${this.routes.size} unanswered` : ''}`);
                this.resolveHandedOver();
            };
            setTimeout(this.leave, HAND_OVER_DRAIN_MS);
            this.leaveIfIdle();
        }

        /**
         * Leaves once nothing is in flight - so no statement is cut off not knowing whether it ran - and no other
         * window's transaction is open. This window's own is not waited for: hidden, its .NET runs on throttled
         * timers, about one continuation a second, so a sync round's transaction could hold every other window for
         * many seconds. It is broken instead, as when a leader closes: its next statement fails, EF rolls back, and
         * the sync round tries again.
         */
        leaveIfIdle() {
            if (!this.leaving || this.routes.size > 0) return;
            if (this.gate.owner === null || this.gate.owner === this.client.tabId) this.leave();
        }

        startFailed(reason) {
            this.worker.terminate();
            if (this.stopped) return;
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
            // Handing over: only the open transaction may carry on, to its end. Anything else is for the next leader.
            if (this.leaving && this.gate.owner !== tab) return;
            const request = { tab, kind, message, transfer };
            if (this.opening) { this.opening.push(request); return; }
            const database = message.data?.database;
            if (database && message.data.type !== 'open' && !this.opened.has(database)) { this.open(database, request); return; }
            this.gate.submit(request);
        }

        /**
         * A window's bridge sends `open` once, when its connection first opens; the worker keeps what is open in memory.
         * A worker started by a hand-over or a close has never been sent it, and answers every statement from a window
         * that was already running "Database mealplanner.db not open" - seen 2026-10-23 on Receipts after switching away
         * from the leading tab. So this opens it first (the worker's open is idempotent), and holds everything after,
         * in order, until it answers: the worker does not finish one message before starting the next.
         */
        open(database, first) {
            note(`opening ${database} for a window that opened it on an earlier worker`);
            this.opening = [first];
            this.post({ tab: null, kind: 'read', message: { data: { type: 'open', database } }, done: ok => {
                if (ok) this.opened.add(database);
                else note(`opening ${database} failed`);
                const held = this.opening;
                this.opening = null;
                for (const r of held) this.submit(r.tab, r.message, r.transfer);
            } });
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
            if (request.done) { request.done(succeeded(data)); this.leaveIfIdle(); return; }   // the leader's own open
            const type = request.message.data?.type, database = request.message.data?.database;
            if (type === 'open' && succeeded(data)) this.opened.add(database);
            else if (type === 'close') this.opened.delete(database);

            const changed = this.gate.completed(request, succeeded(data));
            if (changed) this.client.announceChange(request.tab);
            if (request.tab === null) { this.leaveIfIdle(); return; }            // the leader's own ROLLBACK

            const answer = { ...data, id: request.message.id };
            if (request.tab === this.client.tabId) this.client.deliver(answer);
            else this.client.channel.postMessage({ t: 'res', to: request.tab, msg: answer });
            this.leaveIfIdle();
        }

        /** Learn when a follower window closes: its own lock becomes free. */
        watch(tab) {
            if (this.watched.has(tab)) return;
            this.watched.add(tab);
            navigator.locks.request(TAB_LOCK + tab, { signal: this.watching.signal }, () => {
                this.watched.delete(tab);
                const database = this.databases.get(tab);
                this.databases.delete(tab);
                if (this.gate.tabGone(tab) && database) {
                    this.gate.submit({ tab: null, kind: 'rollback',
                        message: { data: { type: 'execute', database, sql: 'ROLLBACK', parameters: {} } } });
                }
            }).catch(() => {});   // aborted: this window handed the database over, and the next leader watches
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
            this.inLine = null;          // aborts this window's place in line for the leader lock
            this.bridgeReady = false;
            this.pending = new Map();    // request id -> { leaderId, kind, at, message, transfer } sent, not yet answered
            this.leaderlessSince = null; // when this window, started, last found no leader to send to
            this.queued = [];            // requests waiting for a leader
            this.transaction = 'none';   // none | open | broken
            this.transactionLeader = null;
            this.started = false;        // past the reset lock: this window may open the database
            this.resetting = null;       // during a reset: called instead of starting a worker on the leader lock
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
            this.pending.set(message.id, { leaderId: this.leaderId, kind, at: performance.now(), message, transfer });
            if (this.leader) this.leader.submit(this.tabId, message, transfer);
            else this.channel.postMessage({ t: 'req', from: this.tabId, to: this.leaderId, msg: message });
        }

        /**
         * The leader `id` has gone. If it handed over cleanly, nothing still waiting on it ran: it goes again, to whoever
         * leads next - through request(), so a transaction that was open on it still fails, once the next is known.
         * Otherwise it fails then, in leaderIs: nobody can say whether it ran.
         */
        leaderGone(id, clean) {
            if (this.leaderId !== id) return;
            this.leaderId = null;
            this.leaderlessSince = performance.now();
            if (!clean) return;
            for (const [requestId, sent] of [...this.pending]) {
                if (sent.leaderId !== id) continue;
                this.pending.delete(requestId);
                this.request(sent.message, sent.transfer);
            }
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
        async start() {
            // A reset in another window holds this lock until the old files are gone; a window it reloaded, or one
            // opened meanwhile, must not open them first.
            await navigator.locks.request(RESET_LOCK, { mode: 'shared' }, () => {});
            this.started = true;
            this.leaderlessSince = performance.now();
            navigator.locks.request(TAB_LOCK + this.tabId, forever);
            this.queueForLead();
            addEventListener('pagehide', () => { if (this.leader) this.channel.postMessage({ t: 'leader-gone', id: this.tabId }); });
            document.addEventListener('visibilitychange', () => this.visibilityChanged());
            this.channel.postMessage({ t: 'hello', from: this.tabId, visible: !document.hidden });
            note(`started as ${this.tabId.slice(0, 8)}, said hello`);
        }

        /**
         * Wait in line for the leader lock; holding it, run the worker until this window closes or hands over. Only
         * while in view: a suspended window next in line would be given the lock it cannot use - seen when the window
         * in view reloaded beside a suspended one. A hidden window joins the line when it is looked at again, and a
         * profile whose every window is hidden has no leader until one is.
         */
        queueForLead() {
            if (this.inLine || this.leader || document.hidden) return;
            const line = this.inLine = new AbortController();
            navigator.locks.request(LEADER_LOCK, { signal: line.signal }, async () => {
                this.inLine = null;
                if (this.resetting) { note('leader lock granted during a reset'); return this.resetting(); }
                note('leader lock granted, starting worker');
                const leader = this.leader = new Leader(this);
                await leader.ready;
                this.channel.postMessage({ t: 'leader', id: this.tabId });
                this.leaderIs(this.tabId);
                if (document.hidden) this.channel.postMessage({ t: 'who-is-visible' });
                await leader.handedOver;
                // Back in line - if in view - before the lock goes, behind every window already waiting.
                this.queueForLead();
            }).catch(() => { if (this.inLine === line) this.inLine = null; });   // aborted: hidden while in line
        }

        leaveLine() {
            this.inLine?.abort();
            this.inLine = null;
        }

        get leading() { return this.leader !== null && this.leaderId === this.tabId; }

        /**
         * Safari suspends a tab that is not in view: its page and worker stop, yet it keeps the leader lock and the
         * database files, so every other window waits on it until it is looked at again. Seen 2026-10-23 with two
         * windows side by side and the leading one switched to another tab. So the window leading only while it is
         * in view, if another one is: a hidden leader asks who is visible, and hands over when one answers.
         */
        visibilityChanged() {
            if (!this.started) return;
            if (document.hidden) {
                if (this.leading) this.channel.postMessage({ t: 'who-is-visible' });
                else this.leaveLine();
            } else if (!this.leader) {
                this.queueForLead();
                this.channel.postMessage({ t: 'visible', from: this.tabId });
            }
        }

        visibleElsewhere(tab) {
            if (!this.leading || !document.hidden || this.leader.leaving) return;
            note(`hidden, and ${tab.slice(0, 8)} is in view: handing the database over`);
            this.leader.handOver(clean => {
                this.channel.postMessage({ t: 'leader-gone', id: this.tabId, clean });
                this.leader = null;
                this.leaderGone(this.tabId, clean);
            });
        }

        /** Another window is resetting the database: let go of it and reload, to wait for the fresh one. */
        resetElsewhere() {
            if (!this.started || this.resetting) return;
            note('reset by another window, reloading');
            this.resetting = () => {};
            this.leader?.stop();
            location.reload();
        }

        leaderIs(id) {
            if (this.leaderId === id) return;
            note(`leader is ${id === this.tabId ? 'this window' : id.slice(0, 8)}`);
            this.leaderId = id;
            this.leaderlessSince = null;

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
                    if (this.leading) {
                        note(`answered hello from ${m.from.slice(0, 8)}`);
                        this.channel.postMessage({ t: 'leader', id: this.tabId });
                        if (m.visible) this.visibleElsewhere(m.from);
                    }
                    break;
                case 'who-is-visible':
                    if (this.started && !this.leader && !document.hidden) this.channel.postMessage({ t: 'visible', from: this.tabId });
                    break;
                case 'visible':
                    this.visibleElsewhere(m.from);
                    break;
                case 'leader':
                    if (!this.leader) this.leaderIs(m.id);
                    break;
                case 'leader-gone':
                    if (!this.leader) this.leaderGone(m.id, m.clean === true);
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
                case 'reset':
                    this.resetElsewhere();
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
        /**
         * One window at a time starts and migrates. Two windows migrating the one shared database together both
         * read the same history, both apply the next migration, and the second fails SCHEMA_INCOMPATIBLE - seen
         * when a reset reloaded two windows onto an empty database. Resolves once this window holds the turn; it
         * lasts until startDone(), or until the window goes.
         */
        startBegin() {
            this.startSince = performance.now();
            return new Promise(granted => navigator.locks.request(START_LOCK,
                () => new Promise(done => { this.endStart = done; this.startSince = null; granted(); })));
        },
        startSince: null,
        /**
         * Calls dotnetRef.OnWaitingChanged(bool) when this window starts or stops waiting on another window for more
         * than WAITING_LIMIT_MS - most likely one Safari has suspended, which only looking at it, or closing it, wakes.
         */
        onWaiting(dotnetRef) {
            let waiting = false;
            setInterval(() => {
                const c = this.client;
                const now = waitingOnAnotherWindow({
                    now: performance.now(),
                    leading: c?.leader != null,
                    startSince: this.startSince,
                    leaderlessSince: c?.leaderlessSince ?? null,
                    sentSince: c ? [...c.pending.values()].filter(p => p.leaderId !== c.tabId).map(p => p.at) : [],
                }, WAITING_LIMIT_MS);
                if (now === waiting) return;
                waiting = now;
                note(waiting ? 'waiting on another window' : 'no longer waiting on another window');
                dotnetRef.invokeMethodAsync('OnWaitingChanged', waiting);
            }, 1000);
        },
        startDone() {
            this.endStart?.();
            this.endStart = null;
        },
        /** Calls dotnetRef.OnChangedElsewhere() whenever another window commits a change. */
        subscribe(dotnetRef) {
            addEventListener('mealplanner-db-changed', () => dotnetRef.invokeMethodAsync('OnChangedElsewhere'));
        },
        /**
         * Deletes this browser profile's database and reloads every window of the app, which then opens a fresh
         * one. Works whether or not the database opened. On success the page reloads and this never settles;
         * otherwise it resolves to why not, and nothing was deleted.
         */
        reset() {
            return navigator.locks.request(RESET_LOCK, async () => {
                note('reset: started');
                const client = this.client;
                const channel = client?.channel ?? new BroadcastChannel(CHANNEL);
                const tell = () => channel.postMessage({ t: 'reset' });

                // The leader lock is held by whichever window runs the worker; holding it here means none does.
                // Held until `release` - given back if the reset fails, so the other windows are not left without one.
                let release;
                const held = new Promise(r => release = r);
                const abort = new AbortController();
                const taken = new Promise(resolve => {
                    if (client?.leader) { client.leader.stop(); resolve(); return; }
                    const grant = () => { resolve(); return held; };
                    if (client) client.resetting = grant;   // its own queued request may be granted first
                    navigator.locks.request(LEADER_LOCK, { signal: abort.signal }, grant).catch(() => {});
                });
                tell();
                const retell = setInterval(tell, 2000);   // a window that had not started when first told
                const timedOut = new Promise(resolve => setTimeout(() => resolve('timeout'), RESET_TAKE_OVER_MS));
                const outcome = await Promise.race([taken, timedOut]);
                clearInterval(retell);
                if (outcome === 'timeout') {
                    note('reset: another window kept the database');
                    abort.abort();
                    if (client) client.resetting = null;
                    release();
                    return 'Another window of the meal planner is still using this device\'s copy. Close the other ' +
                        'windows of it, reload this one, and try again.';
                }

                try {
                    await removeEntries(await navigator.storage.getDirectory(), DATABASE_ENTRIES,
                        ms => new Promise(r => setTimeout(r, ms)));
                } catch (e) {
                    note(`reset: could not delete the files: ${e?.name}`);
                    release();
                    return `This device's copy could not be deleted (${e?.name ?? e}). Close every window of the ` +
                        'meal planner, open one, and try again.';
                }
                note('reset: deleted, reloading');
                location.reload();
                return forever();   // keeps the reset lock until this page is gone
            });
        },
    };

    root.Worker = class extends RealWorker {
        constructor(url, options) {
            if (String(url).endsWith(WORKER_FILE)) return new Client(url, options);
            super(url, options);
        }
    };
})(globalThis);
