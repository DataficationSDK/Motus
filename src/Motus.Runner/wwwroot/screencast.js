window.motus = window.motus || {};

window.motus.updateFrame = function (base64) {
    const img = document.getElementById('motus-screencast-img');
    if (img) {
        img.src = 'data:image/jpeg;base64,' + base64;
    }
};

window.motus.getImageDimensions = function () {
    const img = document.getElementById('motus-screencast-img');
    if (img) {
        return { width: img.clientWidth, height: img.clientHeight };
    }
    return { width: 0, height: 0 };
};
