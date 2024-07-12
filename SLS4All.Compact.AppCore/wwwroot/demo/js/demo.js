'use strict';

$(document).ready(function () {
    var $body = $('body');

    /*--------------------------------------
        Animation
    ---------------------------------------*/
    var animationDuration;


    $body.on('click', '.animation-demo .card-body .btn', function(){
        var animation = $(this).text();
        var cardImg = $(this).closest('.card').find('img');
        if (animation === "hinge") {
            animationDuration = 2100;
        }
        else {
            animationDuration = 1200;
        }

        cardImg.removeAttr('class');
        cardImg.addClass('animated '+animation);

        setTimeout(function(){
            cardImg.removeClass(animation);
        }, animationDuration);
    });


    /*--------------------------------------
        Icons Preview
    ---------------------------------------*/
    $body.on('click', '.icons-demo__item', function () {
        var icon = $(this).find('span').text();

        $('#icon-preview .icon-size > i').attr('class', 'zwicon-'+icon);
        $('#icon-preview').modal('show');
    });
});