package android.widget;

/*
 * Copyright (C) Contributors to the Suwayomi project
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

import android.content.Context;
import android.text.Editable;
import android.text.TextWatcher;
import android.text.TextUtils;
import android.text.method.MovementMethod;
import java.util.ArrayList;
import java.util.List;

public class EditText {
    private static final Editable.Factory EDITABLE_FACTORY = Editable.Factory.getInstance();

    private final Context context;
    private final List<TextWatcher> textWatchers = new ArrayList<>();
    private Editable text;
    private int inputType;
    private CharSequence error;
    private TextUtils.TruncateAt ellipsizeMode;
    private int selectionStart;
    private int selectionEnd;

    public EditText(Context context) {
        this(context, null);
    }

    public EditText(Context context, android.util.AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public EditText(Context context, android.util.AttributeSet attrs, int defStyleAttr) {
        this(context, attrs, defStyleAttr, 0);
    }

    public EditText(Context context, android.util.AttributeSet attrs, int defStyleAttr, int defStyleRes) {
        this.context = context;
        this.text = EDITABLE_FACTORY.newEditable("");
        this.selectionStart = 0;
        this.selectionEnd = 0;
    }

    public boolean getFreezesText() {
        return true;
    }

    protected boolean getDefaultEditable() {
        return true;
    }

    protected MovementMethod getDefaultMovementMethod() {
        return null;
    }

    public Editable getText() {
        return text;
    }

    public void setText(CharSequence text) {
        setText(text, TextView.BufferType.EDITABLE);
    }

    public void setText(CharSequence text, TextView.BufferType type) {
        CharSequence newText = text == null ? "" : text;
        CharSequence oldText = this.text == null ? "" : this.text;
        notifyBeforeTextChanged(oldText, newText);
        this.text = EDITABLE_FACTORY.newEditable(newText);
        selectionStart = selectionEnd = this.text.length();
        notifyOnTextChanged(oldText, newText);
        notifyAfterTextChanged();
    }

    public void setInputType(int type) {
        this.inputType = type;
    }

    public int getInputType() {
        return inputType;
    }

    public void addTextChangedListener(TextWatcher watcher) {
        if (watcher != null && !textWatchers.contains(watcher)) {
            textWatchers.add(watcher);
        }
    }

    public void removeTextChangedListener(TextWatcher watcher) {
        textWatchers.remove(watcher);
    }

    public void setSelection(int start, int stop) {
        int length = text.length();
        if (start < 0 || stop < 0 || start > length || stop > length) {
            throw new IndexOutOfBoundsException("Invalid selection range");
        }
        selectionStart = Math.min(start, stop);
        selectionEnd = Math.max(start, stop);
    }

    public void setSelection(int index) {
        setSelection(index, index);
    }

    public void selectAll() {
        selectionStart = 0;
        selectionEnd = text.length();
    }

    public void extendSelection(int index) {
        if (index < 0 || index > text.length()) {
            throw new IndexOutOfBoundsException("index out of bounds");
        }
        selectionEnd = index;
    }

    public void setEllipsize(TextUtils.TruncateAt ellipsis) {
        this.ellipsizeMode = ellipsis;
    }

    public TextUtils.TruncateAt getEllipsize() {
        return ellipsizeMode;
    }

    public void setError(CharSequence error) {
        this.error = error;
    }

    public CharSequence getError() {
        return error;
    }

    public java.lang.CharSequence getAccessibilityClassName() {
        return EditText.class.getName();
    }

    public android.view.View getRootView() {
        return null;
    }

    public android.view.ViewParent getParent() {
        return null;
    }

    private void notifyBeforeTextChanged(CharSequence oldText, CharSequence newText) {
        int oldLength = oldText.length();
        int newLength = newText.length();
        for (TextWatcher watcher : textWatchers) {
            watcher.beforeTextChanged(oldText, 0, oldLength, newLength);
        }
    }

    private void notifyOnTextChanged(CharSequence oldText, CharSequence newText) {
        int oldLength = oldText.length();
        int newLength = newText.length();
        for (TextWatcher watcher : textWatchers) {
            watcher.onTextChanged(newText, 0, oldLength, newLength);
        }
    }

    private void notifyAfterTextChanged() {
        for (TextWatcher watcher : textWatchers) {
            watcher.afterTextChanged(text);
        }
    }
}
