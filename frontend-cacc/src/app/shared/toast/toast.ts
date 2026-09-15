import { Component, Input, OnDestroy } from '@angular/core';

export type ToastKind = 'success' | 'error';

// Temporary confirmation, anchored to the bottom right corner of the screen.
//
// The markup and the timings are the ones the payments dashboard introduced in
// HU-015 / HU-016; this component lifts them out so a second screen does not
// have to copy them. That dashboard still carries its own copy: migrating it
// means touching a story that has its own tests, and those cannot be run in this
// environment, so it was left alone on purpose. Once they can be run, it should
// use this component and drop its local version.
@Component({
    selector: 'app-toast',
    standalone: true,
    imports: [],
    template: `
        @if (message) {
            <div class="toast" [class.toast-error]="kind === 'error'" [class.is-leaving]="leaving"
                 role="status" aria-live="polite">
                {{ message }}
            </div>
        }
    `,
    styleUrl: './toast.css',
})
export class Toast implements OnDestroy {
    private static readonly DURATION_MS = 3500;
    private static readonly FADE_MS = 300;

    message = '';
    kind: ToastKind = 'success';
    leaving = false;

    // Setting this shows a toast. Passing the same text twice in a row still
    // restarts the countdown, which is what someone saving twice expects.
    @Input() set notification(value: { text: string; kind: ToastKind } | null) {
        if (value === null) {
            return;
        }
        this.show(value.text, value.kind);
    }

    private fadeTimer?: ReturnType<typeof setTimeout>;
    private clearTimer?: ReturnType<typeof setTimeout>;

    show(text: string, kind: ToastKind = 'success') {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);

        this.message = text;
        this.kind = kind;
        this.leaving = false;

        // The fade starts before the node is removed so it does not blink out
        this.fadeTimer = setTimeout(() => (this.leaving = true), Toast.DURATION_MS - Toast.FADE_MS);
        this.clearTimer = setTimeout(() => {
            this.message = '';
            this.leaving = false;
        }, Toast.DURATION_MS);
    }

    // Leaving the screen mid-animation must not fire a callback on a dead view
    ngOnDestroy() {
        clearTimeout(this.fadeTimer);
        clearTimeout(this.clearTimer);
    }
}
